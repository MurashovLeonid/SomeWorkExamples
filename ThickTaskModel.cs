using System;
using System.IO;
using System.Data;
using System.Web;
using System.Web.Mvc;
using System.Data.SqlClient;
using System.Collections.Generic;
using Portal.Models.CodeModels;
using Portal.Models.Actions;

namespace Portal.Models
{

    public class TaskModel
    {
        Controller controller;

        private dynamic ViewBag
        {
            get { return controller.ViewBag; }
        }

        private HttpRequestBase Request
        {
            get { return controller.Request; }
        }

        public TaskModel(Controller controller)
        {
            this.controller = controller;
        }

        public String Create(HttpRequestBase request)
        {
            string errorInfo = String.Empty;

            try
            {
                Guid ttype = Guid.Empty;
                try
                {
                    ttype = Guid.Parse(Request.Params["task_type"]);
                }
                catch (Exception ex)
                {
                    throw ex;
                }

                // ид задачи
                Guid id = Guid.NewGuid();
                using (StoredProcedure proc = new StoredProcedure("cpSetFileItem"))
                {
                    proc.Assign(new { object_id = id });
                    for (int i = 0; i < request.Files.Count; ++i)
                    {
                        HttpPostedFileBase file = request.Files[i];
                        if ((file == null) || (file.ContentLength == 0)) continue;
                        Byte[] data = new Byte[file.ContentLength];
                        try
                        {
                            file.InputStream.Read(data, 0, file.ContentLength);
                            String text = System.Text.Encoding.ASCII.GetString(data);
                            data = Convert.FromBase64String(text);
                            proc.Parameters["@file_id"].Value = Guid.NewGuid();
                            proc.Parameters["@file_name"].Value = Path.GetFileName(file.FileName);
                            proc.Parameters["@file_size"].Value = file.ContentLength;
                            proc.Parameters["@file_type"].Value = file.ContentType;
                            proc.Parameters["@file_binary"].Value = data;
                            proc.ExecuteNonQuery();
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }

                var result = new System.Text.StringBuilder();
                Object num = null;

                // по задаче: 721560
                // АХУ.Доставка корреспонденции
                if (ttype == Guid.Parse("b08b2d41-0bf7-4dc1-befe-4ee38bffc11c"))
                {
                    string emp_login = Request.Params["emp_login"];
                    string task_desc = Request.Params["task_desc"];
                    DateTime task_start = DateTime.Parse(Request.Params["task_start"]);

                    // получатель
                    string recipient_name = Request.Params["recipient_name"];
                    string recipient_address = Request.Params["recipient_address"];
                    string recipient_fio = Request.Params["recipient_fio"];
                    string recipient_phone = Request.Params["recipient_phone"];

                    // отправитель
                    string sender_name = Request.Params["sender_name"];
                    string sender_address = Request.Params["sender_address"];
                    string sender_landline_phone = Request.Params["sender_landline_phone"];
                    string sender_mobile_phone = Request.Params["sender_mobile_phone"];

                    string guid_1c = Request.Params["guid_1c"];

                    errorInfo = String.Format(@"emp_login={0};task_desc={1};task_start={2};recipient_name={3};recipient_address={4};recipient_fio={5};recipient_phone={6};sender_name={7};sender_address={8};sender_landline_phone={9};sender_mobile_phone={10}",
                        emp_login, task_desc, task_start.ToString(), recipient_name, recipient_address, recipient_fio, recipient_phone, sender_name, sender_address, sender_landline_phone, sender_mobile_phone);

                    DataTable pars = new DataTable();
                    //DataColumn[] keys = new DataColumn[1];
                    String[] cols = { "tpar_id", "tpar_type", "tpar_parent", "tpar_template", "tpar_required", "tpar_order", "tpar_name", "tpar_text", "tpar_deleted", "tpar_obj_id", "tpar_read_only" };
                    foreach (String col in cols)
                        pars.Columns.Add(col);

                    using (SqlConnection cn = new SqlConnection(Settings.CubeConnection))
                    {
                        cn.Open();
                        // получим параметры для создания задачи
                        using (SqlCommand cmd = new SqlCommand("cubeGetAXYKorrespondTaskParams", cn))
                        {
                            cmd.CommandType = System.Data.CommandType.StoredProcedure;
                            cmd.Parameters.AddWithValue("ttype_id", ttype);

                            cmd.Parameters.AddWithValue("recipient_name", recipient_name);
                            cmd.Parameters.AddWithValue("recipient_address", recipient_address);
                            cmd.Parameters.AddWithValue("recipient_fio", recipient_fio);
                            cmd.Parameters.AddWithValue("recipient_phone", recipient_phone);

                            cmd.Parameters.AddWithValue("sender_name", sender_name);
                            cmd.Parameters.AddWithValue("sender_address", sender_address);
                            cmd.Parameters.AddWithValue("sender_landline_phone", sender_landline_phone);
                            cmd.Parameters.AddWithValue("sender_mobile_phone", sender_mobile_phone);

                            using (SqlDataReader reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    DataRow row = pars.Rows.Add(Guid.Parse(reader["tpar_id"].ToString()));
                                    row["tpar_type"] = Convert.IsDBNull(reader["tpar_type"]) ? null : reader["tpar_type"].ToString();
                                    row["tpar_parent"] = Convert.IsDBNull(reader["tpar_parent"]) ? null : reader["tpar_parent"].ToString();
                                    row["tpar_template"] = Convert.IsDBNull(reader["tpar_template"]) ? null : reader["tpar_template"].ToString();
                                    row["tpar_required"] = Convert.IsDBNull(reader["tpar_required"]) ? null : reader["tpar_required"].ToString();
                                    row["tpar_order"] = Convert.IsDBNull(reader["tpar_order"]) ? null : reader["tpar_order"].ToString();
                                    row["tpar_name"] = Convert.IsDBNull(reader["tpar_name"]) ? null : reader["tpar_name"].ToString();
                                    row["tpar_text"] = Convert.IsDBNull(reader["tpar_text"]) ? null : reader["tpar_text"].ToString();
                                    row["tpar_deleted"] = Convert.IsDBNull(reader["tpar_deleted"]) ? null : reader["tpar_deleted"].ToString();
                                    row["tpar_obj_id"] = Convert.IsDBNull(reader["tpar_obj_id"]) ? null : reader["tpar_obj_id"].ToString();
                                    row["tpar_read_only"] = Convert.IsDBNull(reader["tpar_read_only"]) ? null : reader["tpar_read_only"].ToString();
                                }
                            }
                        }
                        // создаем задачу
                        using (SqlCommand cmd = new SqlCommand("cpSetTaskHead", cn))
                        {
                            cmd.CommandType = System.Data.CommandType.StoredProcedure;

                            // возвращаем номер задачи
                            SqlParameter returnParameter = cmd.Parameters.Add("return_value", SqlDbType.Int);
                            returnParameter.Direction = ParameterDirection.ReturnValue;

                            cmd.Parameters.AddWithValue("task_id", id);
                            cmd.Parameters.AddWithValue("task_files", id);
                            cmd.Parameters.AddWithValue("emp_login", emp_login);
                            cmd.Parameters.AddWithValue("task_name", String.Format("{0}{1}", task_desc.Substring(0, Math.Min(task_desc.Length, 50)), task_desc.Length > 50 ? "..." : String.Empty));
                            cmd.Parameters.AddWithValue("task_desc", task_desc);
                            cmd.Parameters.AddWithValue("task_type", ttype);
                            cmd.Parameters.AddWithValue("task_start", task_start);
                            cmd.Parameters.AddWithValue("task_params", pars);

                            cmd.Parameters.AddWithValue("guid_1c", String.IsNullOrWhiteSpace(guid_1c) ? Convert.DBNull : Guid.Parse(guid_1c));

                            #region вычисляемые исполнители
                            if (ttype != Guid.Empty)
                            {
                                try
                                {
                                    DataTable staffs = new DataTable();
                                    staffs.Columns.Add("id");
                                    var calculateStaffs = TaskHelper.GetCalculateStaffs(this.Request.RequestContext.HttpContext, Guid.Parse(ttype.ToString()), pars);
                                    foreach (Guid emp in calculateStaffs)
                                    {
                                        DataRow row = staffs.Rows.Add("id");
                                        row["id"] = emp;
                                    }
                                    if (staffs.Rows.Count > 0)
                                        cmd.Parameters.AddWithValue("calculate_staffs", staffs);
                                }
                                catch (Exception ex)
                                {
                                    // log exception
                                }
                            }
                            #endregion

                            #region вычисляемые наблюдатели
                            if (ttype != Guid.Empty)
                            {
                                try
                                {
                                    DataTable watcher = new DataTable();
                                    watcher.Columns.Add("id");
                                    var calculateWatchers = TaskHelper.GetCalculateWatchers(this.Request.RequestContext.HttpContext, Guid.Parse(ttype.ToString()), pars);
                                    foreach (Guid emp in calculateWatchers)
                                    {
                                        DataRow row = watcher.Rows.Add("id");
                                        row["id"] = emp;
                                    }
                                    if (watcher.Rows.Count > 0)
                                        cmd.Parameters.AddWithValue("calculate_watchers", watcher);
                                }
                                catch (Exception ex)
                                {
                                    // log exception
                                }
                            }
                            #endregion

                            #region вычисляемые подписи
                            if (ttype != Guid.Empty)
                            {
                                try
                                {
                                    DataTable signStaff = new DataTable();
                                    signStaff.Columns.Add("role_id");
                                    signStaff.Columns.Add("sign_order");
                                    signStaff.Columns.Add("sign_need");

                                    var calculateSignStaffs = TaskHelper.GetCalculateSignStaffs(this.Request.RequestContext.HttpContext, Guid.Parse(ttype.ToString()), pars);
                                    foreach (SignStaffTemplateItem item in calculateSignStaffs)
                                        signStaff.Rows.Add(item.sstafftem_role_id, item.sstafftem_order, item.sstafftem_sign_need ? "1" : "0");

                                    if (signStaff.Rows.Count > 0)
                                        cmd.Parameters.AddWithValue("calculate_signStaff", signStaff);
                                }
                                catch (Exception ex)
                                {
                                    // log exception
                                }
                            }
                            #endregion

                            #region вычисляемая подпись руководителя
                            if (ttype != Guid.Empty)
                            {
                                try
                                {
                                    Guid? calculateLeaderSign = TaskHelper.GetCalculateLeaderSign(this.Request.RequestContext.HttpContext, Guid.Parse(ttype.ToString()), pars);
                                    if (calculateLeaderSign.HasValue)
                                        cmd.Parameters.AddWithValue("calculate_leader_sign", calculateLeaderSign);
                                }
                                catch (Exception ex)
                                {
                                    // log exception
                                }
                            }
                            #endregion

                            #region события задачи
                            CalculateTaskEventHelper.Exec(request.RequestContext.HttpContext, CalculateTaskEventHelper.EventTypes.beforeCreateTask, id);
                            #endregion

                            cmd.ExecuteNonQuery();

                            #region события задачи
                            CalculateTaskEventHelper.Exec(request.RequestContext.HttpContext, CalculateTaskEventHelper.EventTypes.afterCreateTask, id);
                            #endregion

                            num = (int)returnParameter.Value;
                        }
                    }
                }
                else
                {
                    #region события задачи
                    CalculateTaskEventHelper.Exec(request.RequestContext.HttpContext, CalculateTaskEventHelper.EventTypes.beforeCreateTask, id);
                    #endregion

                    using (StoredProcedure proc = new StoredProcedure("cpSetTaskHead"))
                    {
                        proc.Assign(Request).Assign(new { task_id = id, task_files = id }).ExecuteNonQuery();
                        num = proc.Parameters["@return_value"].Value;
                    }

                    #region события задачи
                    CalculateTaskEventHelper.Exec(request.RequestContext.HttpContext, CalculateTaskEventHelper.EventTypes.afterCreateTask, id);
                    #endregion
                }

                result.AppendLine(Settings.XmlHeader);
                result.AppendLine("<task>");
                result.AppendLine(String.Format("<task_id>{0}</task_id>", id));
                result.AppendLine(String.Format("<task_number>{0}</task_number>", num));
                result.AppendLine("</task>");

                return result.ToString();
            }
            catch (Exception ex)
            {
                NotifyHelper.SendToAXYTasksErrorAdmins(ex.Message + "|" + errorInfo);
                LogErrorActions.AddLog(ex.Message + "|" + errorInfo, String.Format("method={0};", "Task.Create"), "AXY");
                throw;
            }
        }

        /// <summary>
        /// создание, редактирование, смена типа задачи
        /// </summary>        
        public void Edit(FormCollection form, string procName = "cpSetTaskHead")
        {
            
            Guid return_task_id = Guid.Parse(Request["task_id"]);
            string ttype = Request.Form["task_type"] == null ? Request.QueryString["task_type"] : Request.Form["task_type"];
            try
            {
                // новая задача
                bool isTaskExists = CubeTaskHelper.CheckTaskExists(return_task_id);

                using (StoredProcedure proc = new StoredProcedure(procName))
                {
                    proc.Assign(Request);

                    #region заглушка для АХУ. Дост. Корреспонд.
                    // set task_name for АХУ. Дост. Корреспонд.
                    var tname = proc.Parameters.Contains("@task_name") ? proc.Parameters["@task_name"] : null;
                    if (tname != null && !String.IsNullOrWhiteSpace(ttype) && String.Equals(ttype.Trim(), "b08b2d41-0bf7-4dc1-befe-4ee38bffc11c", StringComparison.OrdinalIgnoreCase))
                    {
                        String value = Request.Form["task_desc"];
                        if (value == null) value = Request.QueryString["task_desc"];
                        tname.Value = String.Format("{0}{1}", value.Substring(0, Math.Min(value.Length, 50)), value.Length > 50 ? "..." : String.Empty);
                    }
                    #endregion

                    #region параметры задачи
                    DataTable pars = new DataTable();
                    pars.Columns.Add("tpar_id");
                    String[] cols = { "tpar_type", "tpar_parent", "tpar_template", "tpar_required", "tpar_order", "tpar_name", "tpar_text", "tpar_deleted", "tpar_obj_id", "tpar_read_only" };
                    foreach (String col in cols) pars.Columns.Add(col);
                    String[] keys = form.GetValues("tpar_id");
                    if (keys != null)
                    {
                        foreach (String key in keys)
                        {
                            // подчиненные datetime параметры помечаем как неактивные/активные, потому что они всегда имеют з-я и не должны сохраняться
                            // if (form["tpar_datetime_disabled" + key] == "true") continue;

                            DataRow row = pars.Rows.Add(key);
                            foreach (String col in cols)
                            {
                                String value = form[col + key];
                                if (String.IsNullOrWhiteSpace(value)) continue;
                                row[col] = value;
                            }

                        }
                    }
                    proc.Parameters["@task_params"].Value = pars;
                    #endregion

                    #region вычисляемые исполнители
                    if (proc.Parameters.Contains("@calculate_staffs"))
                    {
                        if (!String.IsNullOrEmpty(ttype))
                        {
                            try
                            {
                                DataTable staffs = new DataTable();
                                staffs.Columns.Add("id");
                                var calculateStaffs = TaskHelper.GetCalculateStaffs(this.Request.RequestContext.HttpContext, Guid.Parse(ttype.ToString()), pars);
                                foreach (Guid emp in calculateStaffs)
                                {
                                    DataRow row = staffs.Rows.Add("id");
                                    row["id"] = emp;
                                }
                                proc.Parameters["@calculate_staffs"].Value = staffs;
                            }
                            catch (Exception ex)
                            {
                                // log exception
                            }
                        }
                    }
                    #endregion

                    #region вычисляемые наблюдатели
                    if (proc.Parameters.Contains("@calculate_watchers"))
                    {
                        if (!String.IsNullOrEmpty(ttype))
                        {
                            try
                            {
                                DataTable watcher = new DataTable();
                                watcher.Columns.Add("id");
                                var calculateWatchers = TaskHelper.GetCalculateWatchers(this.Request.RequestContext.HttpContext, Guid.Parse(ttype.ToString()), pars);
                                foreach (Guid emp in calculateWatchers)
                                {
                                    DataRow row = watcher.Rows.Add("id");
                                    row["id"] = emp;
                                }
                                proc.Parameters["@calculate_watchers"].Value = watcher;
                            }
                            catch (Exception ex)
                            {
                                // log exception
                            }
                        }
                    }
                    #endregion

                    #region вычисляемые подписи
                    if (proc.Parameters.Contains("@calculate_signStaff"))
                    {
                        try
                        {
                            DataTable signStaff = new DataTable();
                            signStaff.Columns.Add("role_id");
                            signStaff.Columns.Add("sign_need");
                            signStaff.Columns.Add("sign_order");

                            var calculateSignStaffs = TaskHelper.GetCalculateSignStaffs(this.Request.RequestContext.HttpContext, Guid.Parse(ttype.ToString()), pars);
                            foreach (SignStaffTemplateItem item in calculateSignStaffs)
                                signStaff.Rows.Add(item.sstafftem_role_id, (item.sstafftem_sign_need ? "1" : "0"), item.sstafftem_order);

                            proc.Parameters["@calculate_signStaff"].Value = signStaff;
                        }
                        catch (Exception ex)
                        {
                            // log exception
                        }
                    }
                    #endregion

                    #region вычисляемая подпись руководителя
                    if (proc.Parameters.Contains("@calculate_leader_sign"))
                    {
                        try
                        {
                            Guid? calculateLeaderSign = TaskHelper.GetCalculateLeaderSign(this.Request.RequestContext.HttpContext, Guid.Parse(ttype.ToString()), pars);
                            if (calculateLeaderSign.HasValue)
                                proc.Parameters["@calculate_leader_sign"].Value = calculateLeaderSign;
                        }
                        catch (Exception ex)
                        {
                            // log exception
                        }
                    }
                    #endregion
                    
                    #region события задачи
                    CalculateTaskEventHelper.Exec(Request.RequestContext.HttpContext, isTaskExists ? CalculateTaskEventHelper.EventTypes.beforeEditTask : CalculateTaskEventHelper.EventTypes.beforeCreateTask, return_task_id, isTaskExists);
                    #endregion

                    proc.ExecuteNonQuery();

                    #region события задачи
                    CalculateTaskEventHelper.Exec(Request.RequestContext.HttpContext, isTaskExists ? CalculateTaskEventHelper.EventTypes.afterEditTask: CalculateTaskEventHelper.EventTypes.afterCreateTask, return_task_id, isTaskExists);
                    #endregion
                }

                // сохраняем файлы            
                if (return_task_id != Guid.Empty)
                {
                    FileHelper.SaveFiles(Request, return_task_id.ToString(), "Tasks");
                }

                #region вычисляемый автостарт
                if (isTaskExists)
                    TaskHelper.CreateCalculatedAutostartTasks(Request.RequestContext.HttpContext, CalculateAutostartHelper.EventTypes.edit, return_task_id);
                else
                    TaskHelper.CreateCalculatedAutostartTasks(Request.RequestContext.HttpContext, CalculateAutostartHelper.EventTypes.create, return_task_id);
                #endregion

                #region вычисляемые менеджеры
                if (isTaskExists)
                    CalculateManagersHelper.ExecCalcManager(return_task_id, CalculateManagersHelper.EventTypes.edited, Request.RequestContext.HttpContext);
                else
                    CalculateManagersHelper.ExecCalcManager(return_task_id, CalculateManagersHelper.EventTypes.created, Request.RequestContext.HttpContext);
                #endregion

                //#region костыль: задача 218483 - Автокомментарий для задач

                //List<Guid> autoCommentGUIDs = new List<Guid> {
                //    Guid.Parse("30417D3E-0E4C-41C8-A4B1-68B4D77B1C17"), //  AC.УИТ. Анализ/Разработка WebSale.КИАС
	               // Guid.Parse("2120449B-5D7C-4C2D-899E-29D78C498A68"), // 	AC.УИТ. Анализ/Разработка КИАС
	               // Guid.Parse("CC92A4B7-4548-41D0-B600-1D4DBD8BEF97"), // 	АС.УИТ. Доработка WebSale (комплексная)
                //    Guid.Parse("DB9BDED4-E90F-4868-AC54-0520BF1BDAFC"), // 	АС.УИТ. Доработка КИАС (комплексная)
                //    Guid.Parse("BECA7B02-0B7B-4758-8EF8-47DA6691F326"), // 	АС.УИТ. Добавление/изменение функционала сайта компании
                //    Guid.Parse("5DDD793D-B6F1-4836-A6CC-2611758368AF"), //  АС.УИТ. Добавление/изменение функционала WebSale.КИАС 
                //    Guid.Parse("91C9B787-0FFE-454C-8FF4-2690B6E8FAC3"),  //  АС. Доработка модуля партнеров (online.absolutins.ru)
                //    Guid.Parse("00a66c17-9502-4b6b-b2c4-aabfa4cc2f43")  // АС.ДИТ.ЭЛМА. Заявка на разработку, доработку функционала
                //};

                //if (!isTaskExists && return_task_id != Guid.Empty && autoCommentGUIDs.Contains(Guid.Parse(ttype.ToString()))) // Новая задача и ID есть в списке задач
                //{
                //    string mess = @"
                //                Благодарим Вас за обращение!<br/>
                //                Просим учесть, что так как задача имеет тип, относящийся к доработкам информационных сервисов, в работу она будет взята исключительно на основании приоритета, определенного Куратором Вашего направления на технологическом комитете. Со списком сервисов, кураторов подразделений, а также с принципами распределения ресурсов ИТ по направлениям внутри компании можно ознакомиться по ссылке<br/>
                //                <a href='https://wiki.absolutins.ru/pages/viewpage.action?pageId=26740844'>wiki.absolutins.ru</a><br/>
                //                Если вы не являетесь участником технологического комитета, просьба обратиться к непосредственному руководителю для согласования дальнейших шагов по вынесению задачи на технологический комитет.<br/>
                //                Также задать вопрос по задаче можно отправив письмо по адресу <a href='mailto:it.planning@absolutins.ru'>it.planning@absolutins.ru</a>";

                //    // Письмо автору
                //    string email_owner = Settings.emp_mail;
                //    NotifyActions.SetNotify(email_owner, "Support: Автоуведомление", TaskHelper.GetTaskInfoHTML(return_task_id, mess), null);
                //}

                //#endregion


                //#region костыль: задача 458736 - уведомление в TechSupport

                //if (!isTaskExists && ttype.ToString() == "8b7c522b-423f-4b21-aecf-28cb10f4daa9")
                //{
                //    string mess = "Новая задача создана";
                //    string email_owner = "Tehsupport@absgroup.ru";
                //    NotifyActions.SetNotify(email_owner, "Support: Автоуведомление", TaskHelper.GetTaskInfoHTML(return_task_id, mess), null);

                //}

                //#endregion

                // Данные для данного типа задач jira            
                JiraType type = JiraActions.GetJiraTypes(return_task_id);

                //Задача создается в Жира только по кнопке сотрудниками управления сопровождения. 124154
                // синхронизация с Жирой если есть флаг jtt_auto = true
                if (return_task_id != Guid.Empty && type != null && type.jtt_auto)
                {
                    try
                    {
                        // Для отлова ошибки по задаче: http://it.absgroup.ru/browse/WEB-1210
                        LogErrorActions.AddLog("JIRA SyncTask log", String.Format("method={0}; task_id={1}", "TaskModel.Edit", return_task_id.ToString()), "SyncJiraTask");

                        JiraHelper.SyncTask(return_task_id);
                    }
                    catch (Exception ex)
                    {
                        LogErrorActions.AddLog(ex.Message, String.Format("method={0}; task_id={1}", "JiraHelper.SyncTask", return_task_id.ToString()), "jira");
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.GetType() == typeof(TypeOfParentExeption))
                {
                    throw;
                }
                LogErrorActions.AddLog(ex.Message, String.Format("method={0}; procName={1}; return_task_id={2}, ttype={3}", "Edit", procName, return_task_id.ToString(), ttype), "Task");
            }
        }

        public void SignStaff(FormCollection form)
        {
            DataTable dataTable = new DataTable();
            DataColumn[] keys = new DataColumn[1];
            keys[0] = dataTable.Columns.Add("role_id");
            dataTable.Columns.Add("sign_need");
            dataTable.Columns.Add("sign_order");
            dataTable.PrimaryKey = keys;
            String[] roles = form.GetValues("role_id") ?? new String[0];
            String[] needs = form.GetValues("sign_need") ?? new String[0];
            foreach (String role_id in roles)
            {
                if (dataTable.Rows.Find(role_id) != null) continue;
                String need = Array.IndexOf(needs, role_id) < 0 ? "0" : "1";
                dataTable.Rows.Add(role_id, need, form["order" + role_id]);
            }
            roles = form.GetValues("delete") ?? new String[0];
            foreach (String role_id in roles)
            {
                if (dataTable.Rows.Find(role_id) != null) continue;
                dataTable.Rows.Add(role_id, 0, -1);
            }
            using (StoredProcedure proc = new StoredProcedure("cpSetSignStaff_NEW"))
            {
                proc.Assign(Request);
                proc.Parameters["@sign_staff"].Value = dataTable;
                proc.ExecuteNonQuery();
            }

            // проверим настроен ли тип задач на синхранизацию с Жирой
            Guid task_id = Guid.Parse(Request.Params["task_id"]);
            JiraType type = JiraActions.GetJiraTypes(task_id);
            if (type != null)
                // синхронизируем статусы
                JiraHelper.SetJiraTaskState(type, task_id);
        }

        public void TaskStaff(FormCollection form)
        {
            DataTable dataTable = new DataTable();
            DataColumn[] keys = new DataColumn[1];
            keys[0] = dataTable.Columns.Add("emp_id");
            dataTable.Columns.Add("work");
            dataTable.Columns.Add("resp");
            dataTable.PrimaryKey = keys;
            String[] staff = form.GetValues("staff") ?? new String[0];
            String[] works = form.GetValues("work") ?? new String[0];
            String[] resps = form.GetValues("resp") ?? new String[0];
            foreach (String emp_id in staff)
            {
                if (dataTable.Rows.Find(emp_id) != null) continue;
                String work = Array.IndexOf(works, emp_id) < 0 ? "0" : "1";
                String resp = Array.IndexOf(resps, emp_id) < 0 ? "0" : "1";
                dataTable.Rows.Add(emp_id, work, resp);
            }
            using (StoredProcedure proc = new StoredProcedure("cpSetTaskStaff"))
            {
                proc.Assign(Request);
                proc.Parameters["@task_staff"].Value = dataTable;
                proc.ExecuteNonQuery();
            }

            // проверим настроен ли тип задач на синхранизацию с Жирой
            Guid task_id = Guid.Parse(Request.Params["task_id"]);
            JiraType type = JiraActions.GetJiraTypes(task_id);
            if (type != null)
                // синхронизируем статусы
                JiraHelper.SetJiraTaskState(type, task_id);
        }

        public void TaskWatchers(FormCollection form)
        {
            DataTable dataTable = new DataTable();
            DataColumn[] keys = new DataColumn[1];
            keys[0] = dataTable.Columns.Add("emp_id");
            dataTable.Columns.Add("selected");
            dataTable.PrimaryKey = keys;
            String[] staff = form.GetValues("watcher") ?? new String[0];
            String[] works = form.GetValues("selected") ?? new String[0];
            foreach (String emp_id in staff)
            {
                if (dataTable.Rows.Find(emp_id) != null) continue;
                String selected = Array.IndexOf(works, emp_id) < 0 ? "0" : "1";
                dataTable.Rows.Add(emp_id, selected);
            }
            using (StoredProcedure proc = new StoredProcedure("cubeSetTaskWatcher"))
            {
                proc.Assign(Request);
                proc.Parameters["@task_watchers"].Value = dataTable;
                proc.Parameters["@emp_id"].Value = Guid.Parse(Settings.emp_id);
                proc.ExecuteNonQuery();
            }
        }

        public void Make(FormCollection form)
        {
            if (Request.ContentLength > Settings.MaxFileSizeTaskDownload * 1024 * 1024)
            {
                throw new Exception(String.Format("Вложенный файл превышает размер {0} MB", Settings.MaxFileSizeTaskDownload.ToString()));
            }

            using (StoredProcedure proc = new StoredProcedure("cpSetTaskMake"))
            {
                proc.Assign(Request);
                HttpPostedFileBase file = Request.Files["file"];
                if ((file != null) && (file.ContentLength > 0))
                {
                    String name = Path.GetFileName(file.FileName);
                    Byte[] data = new Byte[file.ContentLength];
                    file.InputStream.Read(data, 0, file.ContentLength);

                    proc.Parameters["@binary_name"].Value = name;
                    proc.Parameters["@binary_type"].Value = file.ContentType;
                    proc.Parameters["@binary_value"].Value = data;
                }

                // вычисляем предполагаемую дату закрытия
                var helper = new TaskStateHelper();
                proc.Parameters["@estimatedAutoClosingDate"].Value = helper.GetEstimatingAutoClosingDate(Guid.Parse(Request.Params["task_state"]));
                // кол-во часов для автоматического закрытия задачи
                proc.Parameters["@etimatedAutoClosingTaskHours"].Value = Settings.EtimatedAutoClosingTaskHours;

                proc.ExecuteNonQuery();
            }

            #region вычисляемый автостарт
            Guid return_task_id = Guid.Parse(Request["task_id"]);
            Guid return_task_state = Guid.Parse(Request["task_state"]);
            TaskState task_state = TaskStatesActions.GetTaskSatate(return_task_state, return_task_id);
            switch (task_state.tstate_flags)
            {
                case 8: //выполнение работы
                    TaskHelper.CreateCalculatedAutostartTasks(Request.RequestContext.HttpContext, CalculateAutostartHelper.EventTypes.inWork, return_task_id);
                    break;
                case 256: //на проверку автору
                    TaskHelper.CreateCalculatedAutostartTasks(Request.RequestContext.HttpContext, CalculateAutostartHelper.EventTypes.reviewAuthor, return_task_id);
                    break;
            }
            #endregion

            // проверим настроен ли тип задач на синхранизацию с Жирой
            Guid task_id = Guid.Parse(Request.Params["task_id"]);
            JiraType type = JiraActions.GetJiraTypes(task_id);
            if (type != null)
                // синхронизируем статусы, так-как статус мог быть изменен например при редактировании
                JiraHelper.SetJiraTaskState(type, task_id);
        }

        public void Confirm(FormCollection form)
        {
            Make(form);
        }
        public void Reject(FormCollection form)
        {
            Make(form);
        }

        public void Note(FormCollection form)
        {
            using (StoredProcedure proc = new StoredProcedure("cpSetTaskNote"))
            {
                proc.Assign(Request).ExecuteNonQuery();
            }
        }

        public void Sign(FormCollection form)
        {
            Guid task_id = Guid.Parse(Request.Params["task_id"]);

            #region события задачи
            CalculateTaskEventHelper.Exec(Request.RequestContext.HttpContext, CalculateTaskEventHelper.EventTypes.beforeSignDialog, task_id);
            #endregion

            using (StoredProcedure proc = new StoredProcedure("cpSetTaskSign"))
            {
                proc.Assign(Request);
                String[] values = form.GetValues("sstaff_id") ?? new String[0];
                foreach (String value in values)
                {
                    Guid guid; if (!Guid.TryParse(value, out guid)) continue;
                    proc.Parameters["@sstaff_id"].Value = guid;
                    proc.ExecuteNonQuery();
                }
            }
            // проверим настроен ли тип задач на синхранизацию с Жирой
            JiraType type = JiraActions.GetJiraTypes(task_id);
            if (type != null)
                // синхронизируем статусы
                JiraHelper.SetJiraTaskState(type, task_id);

            // задачи с вычисляемым автостартом
            TaskHelper.CreateCalculatedAutostartTasks(Request.RequestContext.HttpContext, CalculateAutostartHelper.EventTypes.signApprovedOrRejected, task_id);

            #region события задачи
            CalculateTaskEventHelper.Exec(Request.RequestContext.HttpContext, CalculateTaskEventHelper.EventTypes.afterSignDialog, task_id);
            #endregion
        }
    }
}