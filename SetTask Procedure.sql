USE [SupportWeb_Murashov]
GO
/****** Object:  StoredProcedure [dbo].[cpSetTaskHead]    Script Date: 22.03.2022 20:55:02 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
ALTER PROCEDURE  [dbo].[cpSetTaskHead]
(
	@task_id		UNIQUEIDENTIFIER,
	@task_author    UNIQUEIDENTIFIER    = NULL,
	@task_name		VARCHAR(MAX)		= NULL,
	@task_desc		VARCHAR(MAX)		= NULL,
	@task_org		UNIQUEIDENTIFIER	= NULL,
	@task_type		UNIQUEIDENTIFIER	= NULL,
	@task_parent	UNIQUEIDENTIFIER	= NULL,
	@task_start		DATETIME			= NULL,
	@task_deadline	DATETIME			= NULL,
	@task_files		UNIQUEIDENTIFIER	= NULL,
	@task_state		UNIQUEIDENTIFIER	= NULL,
	@tstate_flags	INT					= NULL,
	@log_comment	VARCHAR(MAX)		= NULL,
	@task_params	typeTaskParams READONLY,
	@task_staff		typeEmpLogins READONLY,
	@task_creator	UNIQUEIDENTIFIER	= NULL,
	@emp_login		VARCHAR(256)		= NULL,
	@emp_id			UNIQUEIDENTIFIER	= NULL,
	@helpdesk_theme UNIQUEIDENTIFIER	= NULL,
	@guid_1c		UNIQUEIDENTIFIER	= NULL,
	-- если 1, то не отправляем уведомления, используется например при автозакрытии задачи в техподдержке
	@without_notify	BIT					= 0,
	@calculate_leader_sign UNIQUEIDENTIFIER = NULL, -- вычисляемая подпись руководителя
	@file_delete	typeGuidList READONLY, -- Список удаляемых файлов
	@calculate_staffs typeGuidList READONLY, -- вычисляемые исполнители
	@calculate_watchers typeGuidList READONLY, -- вычисляемые наблюдатели
	@calculate_signStaff typeSignStaff READONLY -- вычисляемые подписи
)
AS
BEGIN
	BEGIN TRANSACTION TaskHead

	DECLARE @task_xml XML = NULL
	DECLARE @task_number INT = NULL
	DECLARE @task_subnum INT = NULL
	DECLARE @fl_created DATETIME = NULL
    DECLARE @task_number_sequence INT = NULL -- Номер задачи в TaskSequence
	DECLARE @task_number_max INT = 1000000 -- Максимальный номер задачи
    DECLARE @task_incident INT = 0 -- Срочная задача
	-- Вычисляем текущего пользователя
	DECLARE @empl_id UNIQUEIDENTIFIER = dbo.fnGetUserId()
	-- Определим автора задачи
	DECLARE @task_owner UNIQUEIDENTIFIER	
	IF @emp_login IS NOT NULL OR @emp_id IS NOT NULL BEGIN	
		-- Если автор задан принудительно, то получаем его
		IF @emp_login IS NULL BEGIN
			IF @emp_id IS NULL BEGIN
				-- текущий пользователь
				SET @task_owner = @empl_id
			END ELSE BEGIN
				-- Автор по id
				SET @task_owner = @emp_id
			END
		END ELSE BEGIN
			-- Автор по логину
			SET @task_owner = (SELECT TOP 1 elgn_emp FROM EmpLogins WHERE elgn_sid = SUSER_SID(@emp_login, 0)) 
		END
	END ELSE BEGIN
		-- Иначе возвращаем реального автора задачи
		SET @task_owner = (SELECT TOP(1) t.task_owner FROM Tasks AS t WHERE t.task_id = @task_id)
		-- Если такой задачи еще нет, то автором явл. текущий пользователь
		IF @task_owner IS NULL BEGIN
			SET @task_owner = @empl_id
		END
	END


	--На время тех.работ, потом можно удалить, и поставить task_author не nullable
	IF @task_author IS NULL 
	SET @task_author = @task_owner

	-- Определим автора задачи, исходя из GUID, который прилетел в @task_author
	DECLARE @task_owner_role UNIQUEIDENTIFIER = NULL
	DECLARE @task_owner_role_name VARCHAR(MAX)
	IF EXISTS (SELECT role_name FROM Roles WHERE role_id = @task_author)
		BEGIN
			SET @task_owner_role = @task_author
			SET @task_owner_role_name = (SELECT role_name FROM Roles WHERE role_id = @task_author)
		END
		
	

	-- для лога
	DECLARE @emp_id_log UNIQUEIDENTIFIER = COALESCE(@emp_id, @empl_id)

	IF @task_creator IS NULL SET @task_creator = @task_owner
	IF @task_creator IS NULL SET @task_creator = @emp_id_log

	--DECLARE @task_owner UNIQUEIDENTIFIER = CASE 
	--	WHEN @emp_login IS NULL THEN @emp_id
	--	ELSE (SELECT TOP 1 elgn_emp FROM EmpLogins WHERE elgn_sid = SUSER_SID(@emp_login, 0)) 
	--END

	-- Выполняем приведение аргументов к типам полей таблицы
	SET @task_name = CAST(@task_name AS VARCHAR(128))
	IF @tstate_flags IS NULL SET @tstate_flags = 0
	DECLARE @task_flag INT = (SELECT TOP 1 tflag_id FROM TaskFlags WHERE tflag_flags = @tstate_flags)

	DECLARE @task_text VARCHAR(MAX) = RTRIM((
		SELECT CAST(tpar_text AS VARCHAR(MAX)) + ' '
		FROM @task_params
		ORDER BY tpar_order, tpar_name
		FOR XML PATH('')
	))

	-- Пробуем получить статус "В очереди на обработку" для данного типа задачи
	-- если он есть, не удалён и настроен первым в порядке 
	DECLARE @queuedProcessingStatus UNIQUEIDENTIFIER = (SELECT TOP 1 tstate_id FROM TaskStates WHERE tstate_ttype_id = @task_type AND fl_deleted IS NULL AND tstate_flags = 1 AND tstate_order = 1)

	
	-- Проверяем существование задачи с заданным ID
	IF EXISTS (SELECT task_id FROM Tasks WITH(READUNCOMMITTED) WHERE task_id = @task_id) BEGIN

	   
		-- Запишем наименование старой роли
		DECLARE @old_task_author_name VARCHAR(MAX)
		IF (SELECT task_owner_role FROM Tasks WHERE task_id = @task_id) IS NOT NULL
			SET @old_task_author_name = (SELECT role_name FROM Roles WHERE role_id = (SELECT task_owner_role FROM Tasks WHERE task_id = @task_id))
		ELSE 
			SET @old_task_author_name = (SELECT emp_name FROM Employee WHERE emp_id = (SELECT task_owner FROM Tasks WHERE task_id = @task_id))

		-- Запишем наименование новой роли
		DECLARE @new_task_author_name VARCHAR(MAX)
		--IF @task_author != (SELECT task_owner FROM Tasks WHERE task_id = @task_id) AND @task_author != (SELECT task_owner_role FROM Tasks WHERE task_id = @task_id)
		IF EXISTS (SELECT task_name FROM Tasks WHERE task_owner = @task_author)
			SET @new_task_author_name = (SELECT emp_name FROM Employee WHERE emp_id = @task_author)
		ELSE
			SET @new_task_author_name = (SELECT role_name FROM Roles WHERE role_id = @task_author)

		-- Проверяем правильность заполнения даты
		SELECT @fl_created = fl_created, @task_number = task_number FROM Tasks WHERE task_id = @task_id
		IF (@task_start IS NULL) OR (@task_start < @fl_created) SET @task_start = @fl_created

		-- Проверяем поле "Крайний срок"
		IF EXISTS (SELECT * FROM TaskTypes WHERE ttype_id = @task_type AND ttype_duration IS NULL) SET @task_deadline = NULL
			ELSE IF (@task_deadline IS NULL) OR (@task_deadline < @task_start) SET @task_deadline = @task_start		

		-- Если тип задачи изменился
		IF NOT EXISTS(SELECT task_id FROM Tasks WHERE task_id = @task_id AND task_type = @task_type) BEGIN
			-- Определяем владельца задачи
			SET @task_owner = (SELECT t.task_owner FROM Tasks AS t WHERE t.task_id = @task_id)

			-- Найдем первоначальное состояние задачи
			-- Проверяем приоритет статуса "В очереди на обработку" над "Анализ" (Если настроен статус и первый в порядке)
			IF (@queuedProcessingStatus IS NOT NULL) BEGIN
				SET @task_state = @queuedProcessingStatus
			END ELSE BEGIN 
				SET @task_state = (SELECT TOP 1 tstate_id FROM TaskStates WHERE tstate_ttype_id = @task_type AND (tstate_flags IN (0,1)) AND fl_deleted IS NULL ORDER BY tstate_flags)
			END 
		
			-- старый тип задачи
			DECLARE @old_ttype_id UNIQUEIDENTIFIER = (SELECT t.task_type FROM Tasks AS t WHERE t.task_id = @task_id)
			
			-- Записываем изменения в шапке задачи
			UPDATE Tasks 
			SET task_type = @task_type, 
				task_name = @task_name, 
				task_desc = @task_desc, 
				task_start = @task_start, 
				task_deadline = @task_deadline, 
				task_state = @task_state, 
				task_text = @task_text, 
				task_helpdesk_category_theme = @helpdesk_theme, 
				task_owner_role = @task_owner_role 
			WHERE task_id = @task_id
			-- запишем в лог информацию о изменении задачи
			EXEC dbo.cubeSetTaskLog 
				@task_id = @task_id
				, @emp_id = @emp_id_log
				, @task_state = @task_state
				, @log_comment = @log_comment
				, @task_name = @task_name
				, @task_desc = @task_desc
				, @task_start = @task_start
				, @task_deadline = @task_deadline
				, @task_params = @task_params
				, @ttype_old_id = @old_ttype_id
				, @ttype_new_id = @task_type
				, @tlog_desc_name = 'Изменен тип задачи.'

			-- Удаляем старые параметры кроме прикрепленных файлов
			DELETE FROM TaskParams WHERE tpar_task_id = @task_id AND tpar_order < 99 AND tpar_binary_value IS NULL

			-- Прикрепленные файлы сдвигаем вниз
			--UPDATE TaskParams SET tpar_order = 99 WHERE tpar_task_id = @task_id AND tpar_order < 99 AND tpar_binary_value IS NOT NULL
			
			-- Сохраняем новые параметры задачи
			EXEC cpFillTaskParams @task_id = @task_id, @task_params = @task_params
			
			-- удаляем непринятые подписи
			DELETE FROM SignStaff
			WHERE sstaff_object_name = 'Tasks' 
				AND sstaff_object_id = @task_id
				AND sstaff_accept NOT IN ('0', '1')

			DELETE FROM TaskStaff WHERE tstaff_task_id = @task_id

			-- Получим текст сообщения для рассылки
			SET @task_xml = dbo.fnTaskNotifyXML(@task_id)
				
			-- Заполнение подписей и исполнителей
			EXEC cpFillTaskHead @task_owner = @task_owner
				, @task_id	= @task_id
				, @task_type = @task_type
				, @task_state = @task_state
				, @task_org = @task_org
				, @task_xml = @task_xml
				, @calculate_staffs = @calculate_staffs
				, @calculate_watchers = @calculate_watchers
				, @calculate_signStaff = @calculate_signStaff
				, @calculate_leader_sign = @calculate_leader_sign
				, @emp_id = @empl_id
				, @without_notify = @without_notify
				
			-- создаем подзадачи помеченные автостартом
			EXEC cpCreateAutostartTasks @parent_task_id	= @task_id

			-- отключаем интеграцию с Жира
			UPDATE JiraTasks SET fl_deleted = GETDATE() WHERE jt_portal = @task_id
		END ELSE BEGIN
			-- Теперь найдем новый статус задачи, если он не задан явным образом
			IF @task_state IS NULL SET @task_state	= (SELECT TOP 1 tstate_id FROM TaskStates (NOLOCK) WHERE tstate_ttype_id = @task_type AND tstate_flags = @tstate_flags AND fl_deleted IS NULL)
				ELSE SET @tstate_flags = (SELECT TOP 1 tstate_flags FROM TaskStates WHERE tstate_id = @task_state)
			-- Если задача комплексная и изменился статус на "выполнена" или "отклонена"
			-- и есть дочерние незакрытые задачи, то закрываем их как и родительскую
			DECLARE @ttype_complex CHAR(1) = (SELECT TOP(1) tt.ttype_complex FROM TaskTypes AS tt WHERE tt.ttype_id = @task_type)
			--@tstate_flags INT = (SELECT TOP(1) ts.tstate_flags FROM TaskStates AS ts WHERE ts.tstate_id = @task_type)
			IF  @ttype_complex = '1' AND @tstate_flags IN (16, -2147483648) BEGIN			
				UPDATE Tasks SET task_state = (SELECT TOP(1) ts2.tstate_id FROM TaskStates AS ts2 WHERE ts2.tstate_ttype_id = Tasks.task_type AND ts2.tstate_flags = @tstate_flags)
				WHERE Tasks.task_parent = @task_id 
				AND Tasks.task_state NOT IN (SELECT ts.tstate_id 
											FROM TaskStates AS ts 
											WHERE ts.tstate_id = Tasks.task_state AND ts.tstate_flags IN (16, -2147483648))
			END
			
		    -- Если поменяли роль автора задачи
			DECLARE @tlog_desc_name_changed_author VARCHAR(MAX) = NULL
			
			BEGIN
				IF @old_task_author_name != @new_task_author_name
				SET @tlog_desc_name_changed_author = 'Роль автора задачи была изменена с ' + @old_task_author_name + ' на ' + @new_task_author_name
				ELSE 
				SET @tlog_desc_name_changed_author = NULL
			END
			EXEC dbo.cubeSetTaskLog 
				@task_id = @task_id
				, @emp_id = @emp_id_log
				, @task_state = @task_state
				, @log_comment = @log_comment
				, @task_name = @task_name
				, @task_desc = @task_desc
				, @task_start = @task_start
				, @task_deadline = @task_deadline
				, @task_params = @task_params
				, @tlog_desc_name = @tlog_desc_name_changed_author
				
			UPDATE Tasks SET task_flag = @task_flag WHERE task_id = @task_id
		
			-- Обновим стандартные поля задачи 
            UPDATE Tasks SET task_type = @task_type 
                    , task_name = ISNULL(@task_name, task_name) 
                    , task_desc = @task_desc 
                    , task_org = ISNULL(@task_org, task_org) 
                    , task_start = ISNULL(@task_start, task_start) 
                    , task_deadline = ISNULL(@task_deadline, task_deadline) 
                    , task_state = ISNULL(@task_state, task_state) 
                    , fl_changed = GETDATE()
					, task_owner_role = @task_owner_role
					, task_helpdesk_category_theme = @helpdesk_theme
            WHERE task_id = @task_id
				
			-- Сохраняем параметры задачи
			EXEC cpFillTaskParams @task_id = @task_id, @task_params = @task_params

		END
	
	END ELSE BEGIN
	
		-- Проверяем правильность заполнения даты
		SET @fl_created = GETDATE()
		IF (@task_start IS NULL) OR (@task_start < @fl_created) SET @task_start = @fl_created

		-- Проверяем поле "Крайний срок"
		IF EXISTS (SELECT * FROM TaskTypes WHERE ttype_id = @task_type AND ttype_duration IS NULL) SET @task_deadline = NULL
			ELSE IF (@task_deadline IS NULL) OR (@task_deadline < @task_start) SET @task_deadline = @task_start

		-- Если подразделение не заполнено, то выбираем основное подразделение пользователя
		IF @task_org IS NULL SET @task_org = (SELECT emp_org FROM Employee WHERE emp_id = @task_owner)

		IF (@task_parent IS NOT NULL) BEGIN 
			-- Подзадача не может ссылаться сама на себя
			IF @task_parent = @task_id SET @task_parent = NULL
			-- Подзадача не может иметь комплексный тип
			IF EXISTS (SELECT * FROM TaskTypes WHERE ttype_id = @task_type AND ttype_complex = '1') SET @task_parent = NULL
		END
		
		-- Вычисляем номер новой задачи
		IF (@task_parent IS NULL) BEGIN 
			INSERT INTO TaskSequence(task_id) VALUES(@task_id)
			SET @task_number = @@IDENTITY
		END ELSE BEGIN
			SET @task_number = (SELECT task_number FROM Tasks WHERE task_id = @task_parent)
			SET @task_subnum = (SELECT ISNULL(MAX(task_subnum), 0) + 1 FROM Tasks WITH(READUNCOMMITTED) WHERE task_number = @task_number)
		END

		-- Найдем первоначальное состояние задачи 
		-- Проверяем приоретет статуса "В очереди на обработку" над "Анализ" (Если настроен статус и первый в порядке)
		IF (@queuedProcessingStatus IS NOT NULL) BEGIN
			SET @task_state = @queuedProcessingStatus
		END ELSE BEGIN 
			SET @task_state = (SELECT TOP 1 tstate_id FROM TaskStates WHERE tstate_ttype_id = @task_type AND (tstate_flags IN (0,1)) AND fl_deleted IS NULL ORDER BY tstate_flags)
		END
	
	    --Если максимальный номер задачи больше 1000000
	    IF (@task_number >= @task_number_max) BEGIN
		   SET @task_number_sequence = @task_number
		   SET @task_number = @task_number - 900000
        END

	    -- Проверяем срочные задачи из HelpDesk
		IF EXISTS (SELECT * FROM HelpDeskCategoryTheme WHERE hdct_id = @helpdesk_theme AND hdct_type = 'incident') SET @task_incident = 1

		-- Создаем новую запись в таблице задач
		INSERT INTO Tasks(task_id, task_number, task_name, task_desc, task_owner, task_state, task_org, task_type, task_parent, task_start, task_deadline, fl_created, task_subnum, task_text, task_helpdesk_category_theme, task_creator, task_number_sequence, task_sla_start, task_incident, guid_1c, task_owner_role)
		   VALUES (@task_id, @task_number, ISNULL(@task_name, ''), @task_desc, @task_owner, @task_state, @task_org, @task_type, @task_parent, @task_start, @task_deadline, @fl_created, @task_subnum, @task_text, @helpdesk_theme, @task_creator, @task_number_sequence, @task_start, @task_incident, @guid_1c, @task_owner_role)

		-- Запишем в лог задачи: Новая задача создана
		INSERT INTO TaskLog(tlog_id, tlog_created, tlog_task_id, tlog_author, tlog_task_state, tlog_desc_name)
			VALUES(NEWID(), GETDATE(), @task_id, @task_creator, @task_state, 'Новая задача создана.')

		-- Можно сразу назначить исполнителей
		IF EXISTS (SELECT * FROM @task_staff) BEGIN
			SET NOCOUNT OFF -- Будем использовать счетчик
			INSERT INTO TaskStaff(tstaff_id, tstaff_task_id, tstaff_emp_id, tstaff_fl_worker, tstaff_fl_responsible, tstaff_comment_to_log)
				SELECT NEWID(), @task_id, e.elgn_emp, '1', '0', 'Исполнитель назначен при создании задачи.'
					FROM (SELECT DISTINCT e.elgn_emp FROM @task_staff AS s INNER JOIN EmpLogins AS e ON e.elgn_sid = SUSER_SID(s.elgn_login, 0)) AS e
			IF @@ROWCOUNT > 0 BEGIN
				DECLARE @comment_to_log	VARCHAR(4000) = 'Исполнители назначены при создании задачи: ' + dbo.fnTaskStaff(@task_id, '1')
				INSERT INTO TaskLog(tlog_id, tlog_created, tlog_task_id, tlog_author, tlog_task_state, tlog_desc_name)
					VALUES(NEWID(), GETDATE(), @task_id, @task_creator, @task_state, @comment_to_log)
			END
		END
		
		-- Сохраняем параметры задачи
		EXEC cpFillTaskParams @task_id = @task_id, @task_params = @task_params
			
		-- Получим текст сообщения для рассылки
		SET @task_xml = dbo.fnTaskNotifyXML(@task_id)
			
		-- Заполнение подписей и исполнителей
		EXEC cpFillTaskHead @task_owner = @task_owner
			, @task_id	= @task_id
			, @task_type = @task_type
			, @task_state = @task_state
			, @task_org = @task_org
			, @task_xml = @task_xml
			, @calculate_staffs = @calculate_staffs
			, @calculate_watchers = @calculate_watchers
			, @calculate_signStaff = @calculate_signStaff
			, @calculate_leader_sign = @calculate_leader_sign
			, @emp_id = @empl_id
			, @without_notify = @without_notify

		-- создаем подзадачи помеченные автостартом
		EXEC cpCreateAutostartTasks @parent_task_id	= @task_id
	END
	
	UPDATE Files SET file_object_name = 'Tasks', file_object_id = @task_id
		WHERE file_object_name = 'Temp' AND file_object_id = @task_files

	-- удаляем файлы, помеченные для удаления и временные файлы, хранящиеся больше суток
	DELETE FROM Files WHERE (file_id IN (SELECT id FROM @file_delete)) OR (file_object_name = 'Temp' AND file_date < (DATEADD(DAY, -1, GETDATE())))
	
	COMMIT TRANSACTION TaskHead

	-- обновляем информацию о сотруднике который перевел задачу в выполнение работы
	EXEC cubeSetTaskDoingEmp @task_id = @task_id
	-- задача на удаленный доступ
	EXEC cpUpdateEmpLastTaskForRemoteControl @task_id = @task_id

	RETURN @task_number
END
