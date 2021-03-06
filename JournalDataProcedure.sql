USE [SupportWeb_Murashov]
GO
/****** Object:  StoredProcedure [dbo].[opJournalData]    Script Date: 22.03.2022 20:59:54 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO



-- @mode: 0 - все, 1 - входящие, 2 - на подпись, 3 - исходящие
-- @text текст задачи, для полнотекстового поиска
-- @originText текст задачи, для like
ALTER PROCEDURE [dbo].[opJournalData]
(
	@emp_id				UNIQUEIDENTIFIER,
	@mode				INT = 1,
	@from				DATE = NULL,
    @till				DATE = NULL,
	@queue				BIT = NULL,	
	@xfrom				DATE = NULL,
    @xtill				DATE = NULL,
	@owner				typeGuidList READONLY,
	@task_owner_role   typeGuidList READONLY,
	@staff				typeGuidList READONLY,
	@responsiblestaff	typeGuidList READONLY,
	@ttype				typeGuidList READONLY,
	@work				typeGuidList READONLY,
	@watcher			typeGuidList READONLY,
	@org				typeGuidList READONLY,
	@state				typeIntegerList READONLY,
	@status				typeNVarcharList READONLY,
	@task_params		typeJournalTaskParams READONLY,
	@text				VARCHAR(MAX) = NULL,	
	@originText			VARCHAR(MAX) = NULL,
	@rank				BIT = NULL,
	@helpdeskCategories typeGuidList READONLY,
	@helpdeskThemes		typeGuidList READONLY,
	-- отправлена на доработку, период
	@rejectStart		DATE = NULL,
    @rejectEnd			DATE = NULL
)
WITH EXECUTE AS OWNER
AS
BEGIN
	SET NOCOUNT ON;

	-- Вычисляем пользователя и его роли
	DECLARE @roles typeGuidList
	INSERT INTO @roles(id) SELECT id FROM dbo.fnEmployeeRoles(@emp_id)
	
	IF @mode IS NULL OR NOT @mode IN (0,1,2,3) SET @mode = 1

	IF @from IS NULL SET @from = '2000-01-01'
	IF @till IS NULL SET @till = '3000-01-01'
	SET @till = DATEADD(DAY, 1, @till)
	
	CREATE TABLE #tasks (id UNIQUEIDENTIFIER)

	DECLARE @sql NVARCHAR(MAX) = N'SELECT DISTINCT Tasks.task_id AS id FROM Tasks (NOLOCK)'
	DECLARE @where NVARCHAR(MAX) = N'WHERE (@from <= Tasks.fl_created) AND (Tasks.fl_created < @till)'

	-- фильтр по статусам
	IF EXISTS(SELECT * FROM @status) BEGIN
		SET @where = @where + (N' AND (TaskStates.tstate_name IN (SELECT txt FROM @status))')		
	END ELSE BEGIN
		DECLARE @stateCode NVARCHAR(MAX) = N''
		IF NOT EXISTS(SELECT * FROM @state) BEGIN
			SET @stateCode = ',0,1,2,4,8,256,512'
		END ELSE BEGIN
			IF EXISTS (SELECT * FROM @state WHERE id = 63) BEGIN SET @stateCode = ',0,1,2,4,8,256,512' END -- входящие(текущие)
			IF EXISTS (SELECT * FROM @state WHERE id = 128) BEGIN SET @stateCode = @stateCode + ',-2147483648' END -- выполненные
			IF EXISTS (SELECT * FROM @state WHERE id = 64) BEGIN SET @stateCode = @stateCode + ',16' END -- отклоненные
			IF EXISTS (SELECT * FROM @state WHERE id = 2) BEGIN SET @stateCode = @stateCode + ',2' END -- на утверждении
			IF EXISTS (SELECT * FROM @state WHERE id = 61) BEGIN SET @stateCode = ',0,1,4,8' END -- в работе
		END
		SET @stateCode = SUBSTRING(@stateCode, 2, LEN(@stateCode))
		SET @where = @where + (N' AND (TaskFlags.tflag_flags IN (' + @stateCode + '))')
	END	

	-- фильтр по дате вывполнения
	IF @xfrom IS NOT NULL OR @xtill IS NOT NULL BEGIN
		IF @xfrom IS NULL SET @xfrom = '2000-01-01'
		IF @xtill IS NULL SET @xtill = '3000-01-01'
		SET @xtill = DATEADD(DAY, 1, @xtill)
		SET @where = @where + (N' 
			AND (@xfrom <= Tasks.task_closed) 
			AND (Tasks.task_closed < @xtill)
		')
	END
	
	-- отправлена на доработку, период
	IF @rejectStart IS NOT NULL OR @rejectEnd IS NOT NULL BEGIN
		IF @rejectStart IS NULL SET @rejectStart = '2000-01-01'
		IF @rejectEnd IS NULL SET @rejectEnd = '3000-01-01'
		SET @rejectEnd = DATEADD(DAY, 1, @rejectEnd)
		SET @where = @where + (N'
			AND (EXISTS(SELECT tl.tlog_id 
						FROM TaskLog AS tl 
							INNER JOIN TaskStates AS ts ON ts.tstate_id = tl.tlog_task_state
							INNER JOIN TaskFlags AS tf ON tf.tflag_flags = ts.tstate_flags
						WHERE tl.tlog_task_id = Tasks.task_id 
							AND tf.tflag_flags = 512
							AND (tl.tlog_created BETWEEN @rejectStart AND @rejectEnd))
				)
		')
	END
	
	-- оцененные задачи
	IF @rank = 1 BEGIN
		SET @where = @where + (N' 
			AND ( Tasks.task_rank IS NOT NULL )
		')
	END

	-- фильтр по параметрам задачи
	IF EXISTS(SELECT tp.tpar_id FROM @task_params AS tp) BEGIN
		DECLARE @cnt INT = (SELECT COUNT(p.tpar_id) FROM @task_params AS p)
		SET @where = @where + (N'
			AND (EXISTS(
					SELECT t.task_id
					FROM Tasks (NOLOCK) AS t
						-- для каждой задачи проверим параметры на совпадение
						OUTER APPLY (SELECT COUNT(tp.tpar_id) AS cnt
									 FROM TaskParams (NOLOCK) AS tp
	 									INNER JOIN @task_params AS inputPar ON tp.tpar_name = inputPar.tpar_name 
											AND (CHARINDEX(CAST(inputPar.tpar_text AS VARCHAR(MAX)), tp.tpar_text_value) > 0 OR CHARINDEX(CAST(inputPar.tpar_text AS VARCHAR(MAX)), tp.tpar_value) > 0)
									 WHERE tp.tpar_task_id = t.task_id) AS tbl
					WHERE tbl.cnt = @cnt AND t.task_id = Tasks.task_id
				)
			)
		')
	END

	-- Подчиненные сотрудники и сам сотрудник
	DECLARE @employees typeGuidList
	INSERT INTO @employees SELECT DISTINCT e.emp_id FROM Employee (NOLOCK) as e INNER JOIN (
		SELECT o.org_id, e.emo_emp_id AS emp_id FROM (
			SELECT DISTINCT r.role_org_id AS org_id
				FROM @roles AS rc INNER JOIN Roles (NOLOCK) as r ON r.role_id = rc.id
				WHERE r.role_signer = '1' AND r.role_org_id IS NOT NULL
			) AS o LEFT JOIN EmpMapOrgs (NOLOCK) AS e ON e.emo_org_id = o.org_id
		) AS o ON e.emp_org = o.org_id OR e.emp_id = o.emp_id
	UNION SELECT @emp_id

	-- Иерархия подразделений
	DECLARE @orgtr typeGuidList
	;WITH Rec(id) AS (
		SELECT id FROM @org UNION ALL 
		SELECT org_id FROM Orgs INNER JOIN Rec ON org_parent_org_id = Rec.id WHERE Orgs.fl_deleted IS NULL
	) INSERT INTO @orgtr(id) SELECT id FROM Rec

	-- Отбор по статусу задачи
	SET @sql = @sql + (N'
		INNER JOIN TaskStates (NOLOCK) ON TaskStates.tstate_id = Tasks.task_state
		INNER JOIN TaskFlags (NOLOCK) ON TaskFlags.tflag_flags = TaskStates.tstate_flags
		INNER JOIN TaskTypes (NOLOCK) ON TaskTypes.ttype_id = Tasks.task_type
	')
	
	-- Входящие (задачи на исполнении у подчиненных сотрудников) 
	IF @mode = 1 BEGIN
		SET @sql = @sql + (N'
			LEFT JOIN TaskStaff (NOLOCK) ON TaskStaff.tstaff_task_id = Tasks.task_id AND TaskStaff.fl_deleted IS NULL
		')
		SET @where = @where + (N' AND (
			TaskTypes.ttype_manager_role_id IN (SELECT id FROM @roles)
			-- задачи у подчиненных сотрудников, подписаны
			OR (TaskStaff.tstaff_emp_id IN (SELECT id FROM @employees) AND (TaskFlags.tflag_flags <> 2))
			-- автор и проверить
			OR (TaskFlags.tflag_flags IN (256) AND Tasks.task_owner IN (SELECT id FROM @employees))
			
			-- исполнитель и на доработке
			OR (TaskFlags.tflag_flags IN (512) AND TaskStaff.tstaff_id IS NOT NULL)
			-- подзадачи у подчиненных сотрудников, подписаны
			OR EXISTS(SELECT DISTINCT ts_child.tstaff_emp_id
				FROM Tasks (NOLOCK) AS t_childs 
					LEFT JOIN TaskStaff (NOLOCK) AS ts_child ON ts_child.tstaff_task_id = t_childs.task_id 
					INNER JOIN TaskStates (NOLOCK) AS tst_child ON tst_child.tstate_id = t_childs.task_state
					INNER JOIN TaskFlags (NOLOCK) AS tf_child ON tf_child.tflag_flags = tst_child.tstate_flags
				WHERE t_childs.task_parent = Tasks.task_id
					AND tf_child.tflag_flags <> 2
					AND ts_child.fl_deleted IS NULL
					AND ts_child.tstaff_emp_id IN (SELECT id FROM @employees))
		)')
	END ELSE IF @mode = 2 BEGIN
		-- на подпись
		SET @sql = @sql + (N'
			INNER JOIN SignStaff (NOLOCK) ON SignStaff.sstaff_object_name = ''Tasks'' 
				AND SignStaff.sstaff_object_id = Tasks.task_id
				AND SignStaff.sstaff_role_id IN (SELECT id FROM @roles)
				AND SignStaff.fl_deleted IS NULL
		')
	END ELSE IF @mode = 3 BEGIN
		-- исходящие
		SET @where = @where + (N' AND (Tasks.task_owner IN (SELECT id FROM @employees)) OR (Tasks.task_owner_role IS NOT NULL AND @emp_id = ANY (SELECT rme_emp_id FROM RoleMapEmployee WHERE rme_role_id = task_owner_role))')	
	END ELSE IF @mode = 0 BEGIN
		-- все задачи

		-- нужно сделать через fnTaskAccess, сейчас падает по таймауту
		--SET @where = @where + (N' AND (
		--	dbo.fnTaskAccess(Tasks.task_id, @emp_id, @roles) = 1
		--)')
		
		IF NOT EXISTS (
			SELECT MAX(oam.oacc_method_admin_fl)
			FROM ObjAccessMethod (NOLOCK) AS oam 
				INNER JOIN @roles AS r ON r.id = oam.oacc_method_role_id
			WHERE oam.oacc_method_obj_name = 'Tasks' AND oam.oacc_method_obj_id IS NULL
			HAVING MAX(oam.oacc_method_admin_fl) % 2 = 1
		) BEGIN
			SET @sql = @sql + (N'
				LEFT JOIN SignStaff (NOLOCK) AS signs ON signs.sstaff_object_name = ''Tasks'' 
					AND signs.sstaff_object_id = Tasks.task_id 
					AND signs.sstaff_role_id IN (SELECT id FROM @roles)
					AND signs.fl_deleted IS NULL 
				LEFT JOIN TaskStaff (NOLOCK) AS staff ON staff.tstaff_task_id = Tasks.task_id 
					AND staff.tstaff_emp_id IN (SELECT id FROM @employees)
					AND staff.fl_deleted IS NULL 
			')
			SET @where = @where + (N' AND (
				-- автор сотрудник или подчиненные
				Tasks.task_owner IN (SELECT id FROM @employees)
				OR signs.sstaff_id IS NOT NULL 
				-- задачи у подчиненных сотрудников и свои
				OR staff.tstaff_id IS NOT NULL
				OR TaskTypes.ttype_manager_role_id IN (SELECT id FROM @roles)
				-- или у пользователя есть права администратора или модератора на тип задачи
				OR EXISTS (SELECT MAX(oam.oacc_method_manager_fl) % 2, MAX(oam.oacc_method_admin_fl) % 2 
							FROM ObjAccessMethod as oam 
								INNER JOIN @roles AS r ON r.id = oam.oacc_method_role_id
							WHERE oam.oacc_method_obj_name = ''TaskTypes'' AND (oam.oacc_method_obj_id = Tasks.task_type OR oam.oacc_method_obj_id IS NULL)			
							HAVING MAX(oam.oacc_method_manager_fl) % 2 = 1 OR MAX(oam.oacc_method_admin_fl) % 2 = 1
				)
				-- подзадачи у подчиненных сотрудников и свои
				OR EXISTS(SELECT DISTINCT ts_child.tstaff_emp_id
					FROM Tasks (NOLOCK) AS t_childs 
						LEFT JOIN TaskStaff (NOLOCK) AS ts_child ON ts_child.tstaff_task_id = t_childs.task_id 
					WHERE t_childs.task_parent = Tasks.task_id						
						AND ts_child.fl_deleted IS NULL
						AND ts_child.tstaff_emp_id IN (SELECT id FROM @employees))				
			)')
		END
	END

	IF @originText IS NOT NULL AND LTRIM(RTRIM(@originText)) <> '' BEGIN
		--SET @where = @where + (N' 
		--	AND CONTAINS((Tasks.task_name, Tasks.task_desc, Tasks.task_text), @text)
		--')
				
		SET @where = @where + (N' 
			AND (
					CHARINDEX(@originText, Tasks.task_name) > 0
					OR CHARINDEX(@originText, Tasks.task_desc ) > 0 
					OR CHARINDEX(@originText, Tasks.task_text ) > 0
			)
		')
	END

	IF EXISTS (SELECT * FROM @staff) BEGIN
		IF @mode <> 1 SET @sql = @sql + (N' LEFT JOIN TaskStaff (NOLOCK) ON TaskStaff.tstaff_task_id = Tasks.task_id AND TaskStaff.fl_deleted IS NULL')
		SET @where = @where + (N' AND (TaskStaff.tstaff_emp_id IN (SELECT id FROM @staff))')
	END ELSE IF @queue = 1 BEGIN
		IF @mode <> 1 SET @sql = @sql + (N' LEFT JOIN TaskStaff (NOLOCK) ON TaskStaff.tstaff_task_id = Tasks.task_id AND TaskStaff.fl_deleted IS NULL')
		SET @where = @where + (N' AND (TaskStaff.tstaff_id IS NULL)')
	END

	IF EXISTS (SELECT * FROM @responsiblestaff) BEGIN
		IF @mode <> 1 SET @sql = @sql + (N' LEFT JOIN TaskStaff (NOLOCK) ON TaskStaff.tstaff_task_id = Tasks.task_id AND TaskStaff.fl_deleted IS NULL AND TaskStaff.tstaff_fl_responsible = 1')
		SET @where = @where + (N' AND (TaskStaff.tstaff_emp_id IN (SELECT id FROM @responsiblestaff))')
	END ELSE IF @queue = 1 BEGIN
		IF @mode <> 1 SET @sql = @sql + (N' LEFT JOIN TaskStaff (NOLOCK) ON TaskStaff.tstaff_task_id = Tasks.task_id AND TaskStaff.fl_deleted IS NULL AND TaskStaff.tstaff_fl_responsible = 1')
		SET @where = @where + (N' AND (TaskStaff.tstaff_id IS NULL)')
	END

	IF EXISTS (SELECT * FROM @work) BEGIN
		SET @where = @where + (N' AND (Tasks.task_worker IN (SELECT id FROM @work))')
	END

	-- наблюдатели
	IF EXISTS (SELECT * FROM @watcher) BEGIN
		SET @where = @where + (N' AND (Tasks.task_id IN (SELECT tw.tw_task_id FROM TaskWatchers AS tw WHERE tw.tw_emp_id IN (SELECT id FROM @watcher) AND tw.fl_deleted IS NULL))')
	END

	IF EXISTS (SELECT id FROM @helpdeskCategories) BEGIN
		SET @where = @where + (N' AND (Tasks.task_helpdesk_category_theme IN (SELECT hdct.hdct_id FROM HelpDeskCategoryTheme (NOLOCK) AS hdct WHERE hdct.hdct_category_id IN (SELECT id FROM @helpdeskCategories)))')
	END

	IF EXISTS (SELECT id FROM @helpdeskThemes) BEGIN
		SET @where = @where + (N' AND (Tasks.task_helpdesk_category_theme IN (SELECT id FROM @helpdeskThemes))')
	END
 

	IF EXISTS (SELECT * FROM @owner) SET @sql = @sql + (N' INNER JOIN @owner AS owners ON owners.id = Tasks.task_owner OR owners.id = Tasks.task_owner_role')	
	IF EXISTS (SELECT * FROM @ttype) SET @sql = @sql + (N' INNER JOIN @ttype AS ttypes ON ttypes.id = Tasks.task_type')
	IF EXISTS (SELECT * FROM @orgtr) SET @sql = @sql + (N' INNER JOIN @orgtr AS orgtr ON orgtr.id = Tasks.task_org')
	
	
	SET @sql = @sql + CHAR(32) + @where + CHAR(32) + 'OPTION(RECOMPILE)'
	
	DECLARE @params NVARCHAR(MAX) = (N'
		@emp_id UNIQUEIDENTIFIER,
		@employees typeGuidList READONLY,
		@roles typeGuidList READONLY,
		@orgtr typeGuidList READONLY,
		@text VARCHAR(MAX) = NULL,
		@originText VARCHAR(MAX) = NULL,
		@from DATE = NULL,
		@till DATE = NULL,
		@xfrom DATE = NULL,
		@xtill DATE = NULL,
		@rejectStart DATE = NULL,
		@rejectEnd DATE = NULL,		
		@cnt INT = 0,
		@owner typeGuidList READONLY,
		@task_owner_role  typeGuidList READONLY,
		@staff typeGuidList READONLY,
		@responsiblestaff typeGuidList READONLY,
		@ttype typeGuidList READONLY,
		@work typeGuidList READONLY,
		@watcher typeGuidList READONLY,
		@org typeGuidList READONLY,
		@status typeNVarcharList READONLY,
		@task_params typeJournalTaskParams READONLY,
		@helpdeskCategories typeGuidList READONLY,
		@helpdeskThemes typeGuidList READONLY
	')

	INSERT INTO #tasks
	EXEC sp_executesql @sql, @params
		, @emp_id = @emp_id
		, @employees = @employees
		, @roles = @roles
		, @text = @text
		, @originText = @originText
		, @from = @from
		, @till = @till
		, @xfrom = @xfrom
		, @xtill = @xtill
		, @rejectStart = @rejectStart
		, @rejectEnd = @rejectEnd
		, @owner = @owner
	    , @task_owner_role = @task_owner_role  
		, @staff = @staff
		, @responsiblestaff = @responsiblestaff
		, @ttype = @ttype
		, @orgtr = @orgtr
		, @status = @status
		, @task_params = @task_params
		, @work = @work
		, @watcher = @watcher
		, @cnt = @cnt
		, @helpdeskCategories = @helpdeskCategories
		, @helpdeskThemes = @helpdeskThemes
		
	SET @sql = (N'
		SELECT DISTINCT CAST(Tasks.task_number AS VARCHAR) + ISNULL(''/'' + CAST(Tasks.task_subnum AS VARCHAR), '''') AS task_number
			, Tasks.task_name, task_desc, dbo.fnShortName(Employee.emp_lname, Employee.emp_fname, Employee.emp_mname) AS emp_name
			, tstate_name, dbo.fnTaskStaff(Tasks.task_id, 0) AS task_staff
	    	, dbo.fnTaskResponsibleStaff(Tasks.task_id, 0)  AS task_responsiblestaff
			, dbo.fnShortName(Worker.emp_lname, Worker.emp_fname, Worker.emp_mname) AS task_worker
			, ttype_name, Tasks.task_rank, Tasks.task_start, task_deadline, task_closed, task_rule, org_sname, task_id, ttype_helpdesk_popup, task_owner_role, role_name AS task_owner_role_name
			, CASE WHEN tstate_flags IN (0,1,2,4,8,256,512) AND task_deadline < GETDATE() THEN 1 ELSE 0 END AS deadline			
			-- информация по последнему этапу
			, last_step.last_step_name
			, last_step.last_step_created
			, last_step.last_step_staffs
			, last_step_hi_level = CASE WHEN last_step.last_step_hi_level = ''1'' THEN ''Да'' ELSE ''Нет'' END
		FROM #tasks AS List
			LEFT JOIN Tasks (NOLOCK) ON Tasks.task_id = List.id
			INNER JOIN TaskStates (NOLOCK) ON TaskStates.tstate_id = Tasks.task_state
			INNER JOIN TaskTypes (NOLOCK) ON TaskTypes.ttype_id = Tasks.task_type
			LEFT JOIN Employee (NOLOCK) ON Employee.emp_id = Tasks.task_owner
			LEFT JOIN Employee (NOLOCK) AS Worker ON Worker.emp_id = Tasks.task_worker
			LEFT JOIN Orgs (NOLOCK) ON Orgs.org_id = Tasks.task_org
			LEFT JOIN Roles (NOLOCK) ON Roles.role_id = Tasks.task_owner_role
			-- последний этап 
			OUTER APPLY (
					SELECT TOP(1) last_step_name = tt_child.ttype_name
						, last_step_created = t_child.fl_created 
						, last_step_staffs = (						
								SELECT STUFF((
									SELECT DISTINCT '', '' + child_staffs.emp_name
									FROM (
											SELECT emp_child.emp_name
											FROM TaskStaff (NOLOCK) AS ts_child
												INNER JOIN Employee (NOLOCK) AS emp_child ON emp_child.emp_id = ts_child.tstaff_emp_id
											WHERE ts_child.tstaff_task_id = t_child.task_id
												AND ts_child.fl_deleted IS NULL
												AND emp_child.fl_deleted IS NULL
									) AS child_staffs
									FOR XML PATH(''''), TYPE).value(''.'', ''NVARCHAR(MAX)''), 1, 1, '''')
								)
						, last_step_hi_level = (SELECT TOP(1) tp_child.tpar_value FROM TaskParams (NOLOCK) AS tp_child WHERE tp_child.tpar_task_id = Tasks.task_id AND tp_child.fl_deleted IS NULL AND tp_child.tpar_name = ''Высокий приоритет'')
					FROM Tasks (NOLOCK) AS t_child
						INNER JOIN TaskTypes (NOLOCK) AS tt_child ON tt_child.ttype_id = t_child.task_type
					WHERE t_child.task_parent = Tasks.task_id
						AND t_child.fl_deleted IS NULL
					ORDER BY t_child.fl_created DESC
			) AS last_step
		ORDER BY Tasks.task_start
		OPTION(RECOMPILE)
	')
	
	EXEC sp_executesql @sql
END



