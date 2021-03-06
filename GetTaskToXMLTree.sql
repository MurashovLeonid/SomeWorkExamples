USE [SupportWeb_Murashov]
GO
/****** Object:  StoredProcedure [dbo].[cpGetTaskXML]    Script Date: 22.03.2022 20:56:34 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


	ALTER PROCEDURE [dbo].[cpGetTaskXML] (
	@task_id UNIQUEIDENTIFIER			= NULL,
	@task_src UNIQUEIDENTIFIER			= NULL,
	@task_type UNIQUEIDENTIFIER			= NULL,
	@task_parent UNIQUEIDENTIFIER		= NULL,
	@full BIT							= 1,
	@mode INT							= NULL,
	@task_tmpl UNIQUEIDENTIFIER			= NULL,
	@helpdesk_theme UNIQUEIDENTIFIER	= NULL,
	@emp_id UNIQUEIDENTIFIER			= NULL,
	@event	VARCHAR(10)					= NULL
)
AS
BEGIN
	SET NOCOUNT ON;

	IF @mode IS NULL BEGIN
		SET @mode = @full
	END ELSE BEGIN
		SET @full = CASE WHEN @mode = 0 THEN 0 ELSE 1 END
	END

	-- Вычисляем пользователя и его роли
	DECLARE @roles typeGuidList
	IF @emp_id IS NULL SET @emp_id = dbo.fnEmployeeId()
	INSERT INTO @roles(id) SELECT id FROM dbo.fnEmployeeRoles(@emp_id)

	DECLARE @task_author_name VARCHAR(MAX)
	DECLARE @task_author UNIQUEIDENTIFIER


	IF @task_id IS NULL OR NOT EXISTS (SELECT * FROM Tasks (NOLOCK) WHERE task_id = @task_id) BEGIN
		-- Это новая задача

		IF @task_id IS NULL 
		BEGIN
			SET @task_id = NEWID()
			DECLARE @task_author_role_list UNIQUEIDENTIFIER

			-- Вычислим список ролей автора задачи, среди которых есть роли с параметром role_task_owner = 1

			SET @task_author_role_list = (SELECT TOP (1) role_id FROM Roles
									   WHERE role_id IN 
									   (SELECT role_id FROM Roles 
									   WHERE role_task_owner = 1 
									   INTERSECT (SELECT rme_role_id FROM RoleMapEmployee 
									   WHERE rme_emp_id = @emp_id)))
			 IF ((SELECT COUNT(role_id) FROM Roles 
				WHERE role_id IN 
				(SELECT TOP (1) role_id FROM Roles
				WHERE role_id IN (SELECT role_id FROM Roles WHERE role_task_owner = 1 INTERSECT (SELECT rme_role_id FROM RoleMapEmployee WHERE rme_emp_id = @emp_id)))) > 0)
			BEGIN
				SET @task_author = @task_author_role_list
				SET @task_author_name = (SELECT role_name FROM Roles WHERE role_id = @task_author)
			END
			ELSE
			BEGIN
			    SET @task_author = @emp_id
				SET @task_author_name = (SELECT emp_name FROM Employee WHERE emp_id = @task_author)
			END									   			
		END
		
		-- Запрет копирования задач, если тип задачи помечен на удаление
		IF EXISTS (SELECT * FROM Tasks (NOLOCK) AS t INNER JOIN TaskTypes (NOLOCK) AS tt ON t.task_type = tt.ttype_id AND tt.fl_deleted IS NOT NULL WHERE t.task_id = @task_src) SET @task_src = NULL

		IF @task_src IS NOT NULL BEGIN
			-- определим тип задач, который выбран в комбо
			DECLARE @ttype_selected UNIQUEIDENTIFIER 
			IF @task_type IS NOT NULL BEGIN
				SET @ttype_selected = @task_type
			END ELSE BEGIN
				SET @ttype_selected = (SELECT t.task_type FROM Tasks AS t WHERE t.task_id = @task_src)
			END

			-- Копирование задачи
			SELECT [header/title] = CASE WHEN @task_parent IS NULL THEN 'Новая задача' ELSE 'Новая подзадача' END
				, [header/obj_name] = 'Tasks'
				, [header/obj_id] = LOWER(@task_id)
				, [header/task_src] = LOWER(@task_src)
				, (SELECT LOWER(@task_id) AS task_id

						, dbo.fnFormatDate(GETDATE()) AS task_start
						, tt.ttype_start_caption

						, dbo.fnFormatDate(DATEADD(DAY, ISNULL(tt.ttype_duration, 1), GETDATE())) AS task_deadline
						, tt.ttype_deadline_caption

						, tt.ttype_start_read_only
						, tt.ttype_deadline_read_only

						-- если true, то показываем попап техподдержки
						, ttype_helpdesk_popup = (SELECT tt.ttype_helpdesk_popup FROM TaskTypes AS tt WHERE tt.ttype_id = @ttype_selected)
						-- дефолтная хелпдеск тема
						, ttype_helpdesk_default_theme = (SELECT tt.ttype_helpdesk_default_theme FROM TaskTypes AS tt WHERE tt.ttype_id = @ttype_selected)

						, tt.ttype_file_required
						, tt.ttype_file_html_title
						, @task_author AS task_author
						, @task_author_name AS task_author_name
						, tt.ttype_curr_staff
						, LOWER(e.emp_id) AS task_owner
						, LOWER(tt.ttype_id) AS task_type
						, LOWER(o.org_id) AS task_org
						, LOWER(e.emp_id) AS emp_id
						, LOWER(o.org_id) AS org_id
						, t.task_name
						, REPLACE(t.task_desc, '<BR>', CHAR(10)) AS task_desc
						, tt.ttype_name
						, tt.ttype_desc
						, tt.ttype_duration
						, o.org_name
						, LOWER(tt.ttype_make_id) AS ttype_make_id
						, LOWER(tt.ttype_confirm_id) AS ttype_confirm_id
						, task_parent = dbo.fnTaskParent(t.task_parent)
						, LOWER(t.task_helpdesk_category_theme) AS task_helpdesk_category_theme
						, params = CASE WHEN @task_type IS NULL OR @task_type = t.task_type
							THEN dbo.fnTaskParams(2, t.task_id, @task_parent, NULL, NULL, NULL, @event) 
							ELSE dbo.fnTaskParams(1, NULL, @task_parent, NULL, NULL, @task_type, @event) 
						END
					FROM 
						Tasks (NOLOCK) AS t
							LEFT JOIN Employee (NOLOCK) AS e ON e.emp_id = @emp_id
							LEFT JOIN Orgs (NOLOCK) AS o ON o.org_id = e.emp_org
							LEFT JOIN TaskTypes (NOLOCK) AS tt ON tt.ttype_id = t.task_type
					WHERE 
						t.task_id = @task_src
					FOR XML PATH('task'), TYPE
			) FOR XML PATH('tasks'), TYPE		
		END ELSE IF @task_tmpl IS NOT NULL BEGIN
			-- Заполнение по шаблону
			SELECT [header/title] = CASE WHEN @task_parent IS NULL THEN 'Новая задача' ELSE 'Новая подзадача' END
				, [header/obj_name] = 'Tasks'
				, [header/obj_id] = LOWER(@task_id)				
				, (SELECT LOWER(@task_id) AS task_id

						, dbo.fnFormatDate(GETDATE()) AS task_start
						, tt.ttype_start_caption

						, dbo.fnFormatDate(DATEADD(DAY, ISNULL(tt.ttype_duration, 1), GETDATE())) AS task_deadline
						, tt.ttype_deadline_caption

						, tt.ttype_start_read_only
						, tt.ttype_deadline_read_only

						-- если true, то показываем попап техподдержки
						, tt.ttype_helpdesk_popup
						-- дефолтная хелпдеск тема
						, tt.ttype_helpdesk_default_theme

						, tt.ttype_file_required
						, tt.ttype_file_html_title
						, @task_author AS task_author
						, @task_author_name AS task_author_name
						, tt.ttype_curr_staff
						, LOWER(e.emp_id) AS task_owner
						, LOWER(tt.ttype_id) AS task_type
						, LOWER(o.org_id) AS task_org
						, LOWER(e.emp_id) AS emp_id
						, LOWER(o.org_id) AS org_id
						, t.ttem_task_name as task_name
						, REPLACE(t.ttem_task_desc, '<BR>', CHAR(10)) AS task_desc
						, tt.ttype_name
						, tt.ttype_desc
						, tt.ttype_duration
						, o.org_name
						, LOWER(tt.ttype_make_id) AS ttype_make_id
						, LOWER(tt.ttype_confirm_id) AS ttype_confirm_id
						, params = dbo.fnTaskParams(3, NULL, @task_parent, NULL, t.ttem_id, NULL, @event)
						--, task_parent = dbo.fnTaskParent(t.task_parent)						
					FROM 
						TaskTemplates (NOLOCK) AS t
							LEFT JOIN Employee (NOLOCK) AS e ON e.emp_id = @emp_id
							LEFT JOIN Orgs (NOLOCK) AS o ON o.org_id = e.emp_org
							LEFT JOIN TaskTypes (NOLOCK) AS tt ON tt.ttype_id = t.ttem_task_type
					WHERE 
						t.ttem_id = @task_tmpl
					FOR XML PATH('task'), TYPE
			) FOR XML PATH('tasks'), TYPE	
		END ELSE BEGIN
			-- Создание новой задачи			
			DECLARE @task_deadline VARCHAR(MAX)
			DECLARE @task_start VARCHAR(MAX)
			DECLARE @task_name VARCHAR(MAX)
			DECLARE @task_desc VARCHAR(MAX)
			IF(@task_parent IS NOT NULL) BEGIN
				SET @task_deadline = dbo.fnSubtaskParam(@task_type, @task_parent, NULL, 'task_deadline')
				SET @task_start = dbo.fnSubtaskParam(@task_type, @task_parent, NULL, 'task_start')
				SET @task_name = dbo.fnSubtaskParam(@task_type, @task_parent, NULL, 'task_name')
				SET @task_desc = dbo.fnSubtaskParam(@task_type, @task_parent, NULL, 'task_desc')
			END

			SELECT [header/title] = CASE WHEN @task_parent IS NULL THEN 'Новая задача' ELSE 'Новая подзадача' END
				, [header/obj_name] = 'Tasks'
				, [header/obj_id] = LOWER(@task_id)
				, (SELECT [obj_name] = 'Tasks'
						, [obj_id] = LOWER(@task_id)
						, LOWER(@task_id) AS task_id
						
						, ISNULL(@task_start, dbo.fnFormatDate(GETDATE())) AS task_start
						, tt.ttype_start_caption

						, ISNULL(@task_deadline, dbo.fnFormatDate(DATEADD(DAY, ISNULL(tt.ttype_duration, 1), GETDATE()))) AS task_deadline
						, tt.ttype_deadline_caption

						, tt.ttype_start_read_only
						, tt.ttype_deadline_read_only

						-- если true, то показываем попап техподдержки
						, tt.ttype_helpdesk_popup
						-- дефолтная хелпдеск тема
						, tt.ttype_helpdesk_default_theme
						, tt.ttype_file_required
						, tt.ttype_file_html_title

						, tt.ttype_curr_staff
						, @task_author AS task_author
						, @task_author_name AS task_author_name
						, LOWER(e.emp_id) AS task_owner
						, LOWER(tt.ttype_id) AS task_type
						, LOWER(o.org_id) AS task_org
						, LOWER(o.org_id) AS org_id
						, LOWER(e.emp_id) AS emp_id
						, LOWER(o.org_id) AS org_id
						, tt.ttype_name
						, tt.ttype_desc
						, tt.ttype_duration
						, o.org_name
						, LOWER(tt.ttype_make_id) AS ttype_make_id
						, LOWER(tt.ttype_confirm_id) AS ttype_confirm_id
						, @task_name AS task_name
						, @task_desc AS task_desc
						, task_parent = dbo.fnTaskParent(@task_parent)
						, LOWER(@helpdesk_theme) AS task_helpdesk_category_theme
						, params = dbo.fnTaskParams(1, NULL, @task_parent, NULL, NULL, tt.ttype_id, @event)
					FROM 
						Employee (NOLOCK) AS e
							LEFT JOIN Orgs (NOLOCK) AS o ON o.org_id = e.emp_org
							LEFT JOIN TaskTypes (NOLOCK) AS tt ON tt.ttype_id = @task_type
					WHERE 
						e.emp_id = @emp_id
					FOR XML PATH('task'), TYPE
			) FOR XML PATH('tasks'), TYPE
		END

	END ELSE BEGIN

		-- Проверим права на просмотр
		IF dbo.fnTaskAccess(@task_id, @emp_id, @roles, 1) = 0 RETURN 0
		-- Запишем в журнал событий
		EXEC [dbo].[cpAddEventLogs] @emp_id = @emp_id, @obj_name = 'Tasks', @obj_id = @task_id, @code = 'VIEW'
		-- Пометим задачу как посещенную
		IF NOT EXISTS (SELECT rv_emp_id FROM RegVisited WHERE rv_emp_id = @emp_id AND rv_task_id = @task_id) 
			INSERT INTO RegVisited(rv_emp_id, rv_task_id) SELECT @emp_id, @task_id
			
		-- тип задач
		DECLARE @ttype UNIQUEIDENTIFIER = (SELECT TOP(1) t.task_type FROM Tasks AS t WHERE t.task_id = @task_id)
		-- Проверим права администратора
		DECLARE @admin BIT = dbo.fnObjectAdmin(@roles, 'Tasks', NULL)
		IF @admin IS NULL OR @admin = 0 BEGIN
			SET @admin = dbo.fnObjectAdmin(@roles, 'TaskTypes', @ttype)
		END
		

		-- Выборка данных
		SELECT (SELECT [title] = CASE WHEN Tasks.task_parent IS NULL THEN 'Задача' ELSE 'Подзадача' END
				+ ' № ' + CAST(Tasks.task_number AS VARCHAR) + ISNULL('/' + CAST(Tasks.task_subnum AS VARCHAR), '')
				+ ' от ' + dbo.fnFormatDate(Tasks.fl_created)
				, [obj_name] = 'Tasks'
				, [obj_id] = LOWER(Tasks.task_id)
				FOR XML PATH('header'), TYPE)
			, dbo.fnTaskXML(Tasks.task_id, @emp_id, @roles, @admin, @mode, @task_type, @event) 
		FROM Tasks (NOLOCK)
		WHERE Tasks.task_id = @task_id
		FOR XML PATH('tasks'), TYPE
	END
END
