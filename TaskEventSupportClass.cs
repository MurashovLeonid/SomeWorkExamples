using System;
using System.Web;
using System.Data;
using System.Collections.Generic;
using System.Linq;
using Portal.Models.Actions;
using EF = Models.EF;
using Newtonsoft.Json;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Web.Mvc;
using System.Web.Http;
using System.Data.SqlClient;
using Portal.Models.CodeModels;
using System.Web.Script.Serialization;

namespace Portal
{
    /// <summary>
    /// Хелпер для перехвата событий задач
    /// TODO:
    /// при создании не рейзится событие
    /// при редактировании убери (json != lastLog.evlg_data) ибо жсон надо из реквеста а не из базы
    ///  
    /// Примеры смотри ниже
    /// </summary>
    public static class CalculateTaskEventHelper
    {
        /// <summary>
        /// возможные события
        /// </summary>
        public enum EventTypes
        {
            // post формы создания задачи
            beforeCreateTask = 1, // до вызова sp
            afterCreateTask = 2, // после вызова sp
            // post формы редактирования задачи
            beforeEditTask = 3, // до вызова sp
            afterEditTask = 4, // после вызова sp
            // post формы MakeDialog(кнопка "выполнить"), работает и для старого и нового просмотров задач
            beforeMakeDialog = 5, // до вызова sp
            afterMakeDialog = 6, // после вызова sp
            // post формы SignDialog(кнопка "согласовать/отклонить"), работает и для старого и нового просмотров задач
            beforeSignDialog = 7, // до вызова sp
            afterSignDialog = 8, // после вызова sp
            // post формы SignStaffDialog(кнопка "запросить подпись"), работает и для старого и нового просмотров задач
            beforeSignStaffDialog = 9, // до вызова sp
            afterSignStaffDialog = 10, // после вызова sp
        }

        /// <summary>
        /// фильтр обработчиков по типам задач
        /// </summary>        
        public static void Exec(HttpContextBase context, EventTypes eventType, Guid task_id, bool isTaskexists = true)
        {
            try
            {
                var ttype = Guid.Parse(TaskActions.GetTaskType(task_id).ToString());
                //var ttype = Guid.Parse(context.Request.Params["task_type"]);

                // тип задач TEST http://admin.absgroup.ru/view/TaskTypes/0E551C08-A094-4A80-A47D-686DAC82B528
                if (ttype == Guid.Parse("0E551C08-A094-4A80-A47D-686DAC82B528"))
                {
                    TestTtype(context, eventType, task_id);
                }
                // тип задач ДРП. Заявка на подбор персонала http://admin.absgroup.ru/view/TaskTypes/b5ae5d08-6224-462a-8a86-3d1c400accec
                if (ttype == Guid.Parse("b5ae5d08-6224-462a-8a86-3d1c400accec"))
                {
                    Recruitmenttype(context, eventType, task_id);
                }

                if ((ttype == Guid.Parse("4e0e6dfc-7e9e-43a2-b025-67d0993ae376")) && eventType == EventTypes.beforeCreateTask)
                {
                    if(!IsRightParent(Guid.Parse(context.Request.Params["task_parent"])))
                        throw new TypeOfParentExeption("Данный тип подзадач может создаваться только, если родительской задачей является ДИТР.Новый сотрудник"); 
                   
                }
                if (isTaskexists == false && eventType == EventTypes.afterCreateTask)
                {
                    AutoCommentForAbSecTasks(context, eventType, task_id);
                }
                if (isTaskexists == false && eventType == EventTypes.afterCreateTask && ttype.ToString() == "cc45129a-f377-4e92-b5a6-e8d36f875f0a")
                {
                    NotifyToTechSupport(context, eventType, task_id);
                }
                if (isTaskexists == false && eventType == EventTypes.afterCreateTask)
                {
                    NotifyToAllUsersInTaskAuthorRole(context, eventType, task_id);
                }


                //if (ttype == Guid.Parse("8c6165f9-048b-418a-8702-88a166c57489") || ttype == Guid.Parse("6e6f24f0-d05d-4188-8467-8b282e5f6ad6"))
                //{
                //    CreateWork1CServer(context, eventType, task_id);
                //}

                //07
                if (ttype == Guid.Parse("3b3eaf0e-c459-487f-b5f2-a584ff878a58") || ttype == Guid.Parse("e5fdedae-655d-44ba-981e-6a4b916a982a"))
                {
                    CreateWork1CServer(context, eventType, task_id);
                } 
                if (ttype == Guid.Parse("ECD62D93-500E-4945-B849-6A932115C57E"))
                {
                    UpdateTest1CServer(context, eventType, task_id);
                }
                // 	ДИТР. Обновление платформы на рабочих серверах
                if (ttype == Guid.Parse("c54e33f2-dabe-42aa-8f2f-20d5114ebfd2"))
                {
                    
                }
                // другие ttypes ...
                // тип задач ДИТР. Обновление платформы на тестовых РМ и ДИТР. Обновление платформы на тестовых серверах


                // другие ttypes
                // ДИТР новый сотрудник


            }


            catch (Exception ex)
            {
                if (ex.GetType() == typeof(TypeOfParentExeption))
                {
                    throw;
                }
                LogErrorActions.AddLog(ex.Message, String.Format("method={0}; task_id={1}", "CalculateTaskEventHelper.Exec", task_id.ToString()), "Task");
            }
        }

       public static bool IsRightParent(Guid parent)
        {   
            using (SqlConnection cn = new SqlConnection(Settings.CubeConnection))
            {
                cn.Open();
                string sql = @"SELECT t.task_type FROM Tasks as t 
                                       WHERE t.task_id = @parentTask";
                using (SqlCommand cmd = new SqlCommand(sql, cn))
                {
                    cmd.Parameters.AddWithValue("parentTask", parent);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            return  Guid.Parse(reader["task_type"].ToString()) == Guid.Parse("cf972438-7223-4c50-a95e-28dccba242bb");
                        }
                    }                                  
                }
            }
            return false;
        }
       

        #region костыль: задача 218483 - Автокомментарий для задач
        public static void AutoCommentForAbSecTasks(HttpContextBase context, EventTypes eventType, Guid ttype)
        {
            List<Guid> autoCommentGUIDs = new List<Guid> {
                    Guid.Parse("30417D3E-0E4C-41C8-A4B1-68B4D77B1C17"), //  AC.УИТ. Анализ/Разработка WebSale.КИАС
	                Guid.Parse("2120449B-5D7C-4C2D-899E-29D78C498A68"), // 	AC.УИТ. Анализ/Разработка КИАС
	                Guid.Parse("CC92A4B7-4548-41D0-B600-1D4DBD8BEF97"), // 	АС.УИТ. Доработка WebSale (комплексная)
                    Guid.Parse("DB9BDED4-E90F-4868-AC54-0520BF1BDAFC"), // 	АС.УИТ. Доработка КИАС (комплексная)
                    Guid.Parse("BECA7B02-0B7B-4758-8EF8-47DA6691F326"), // 	АС.УИТ. Добавление/изменение функционала сайта компании
                    Guid.Parse("5DDD793D-B6F1-4836-A6CC-2611758368AF"), //  АС.УИТ. Добавление/изменение функционала WebSale.КИАС 
                    Guid.Parse("91C9B787-0FFE-454C-8FF4-2690B6E8FAC3"),  //  АС. Доработка модуля партнеров (online.absolutins.ru)
                    Guid.Parse("00a66c17-9502-4b6b-b2c4-aabfa4cc2f43")  // АС.ДИТ.ЭЛМА. Заявка на разработку, доработку функционала
                };

            if (autoCommentGUIDs.Contains((ttype))) // Новая задача и ID есть в списке задач
            {
                string mess = @"
                                Благодарим Вас за обращение!<br/>
                                Просим учесть, что так как задача имеет тип, относящийся к доработкам информационных сервисов, в работу она будет взята исключительно на основании приоритета, определенного Куратором Вашего направления на технологическом комитете. Со списком сервисов, кураторов подразделений, а также с принципами распределения ресурсов ИТ по направлениям внутри компании можно ознакомиться по ссылке<br/>
                                <a href='https://wiki.absolutins.ru/pages/viewpage.action?pageId=26740844'>wiki.absolutins.ru</a><br/>
                                Если вы не являетесь участником технологического комитета, просьба обратиться к непосредственному руководителю для согласования дальнейших шагов по вынесению задачи на технологический комитет.<br/>
                                Также задать вопрос по задаче можно отправив письмо по адресу <a href='mailto:it.planning@absolutins.ru'>it.planning@absolutins.ru</a>";

                // Письмо автору
                string email_owner = Settings.emp_mail;
                NotifyActions.SetNotify(email_owner, "Support: Автоуведомление", TaskHelper.GetTaskInfoHTML(Guid.Parse(context.Request.Params["task_type"]), mess), null);
            }
        }
        #endregion

        #region костыль: задача 458736 - уведомление в TechSupport
        public static void NotifyToTechSupport(HttpContextBase context, EventTypes eventType, Guid task_id)
        {
            string mess = "Новая задача создана";
            string email_owner = "Tehsupport@absgroup.ru";
            NotifyActions.SetNotify(email_owner, "Support: Автоуведомление", TaskHelper.GetTaskInfoHTML(task_id, mess), task_id);
        }

        #endregion

        #region задача по ДПН 445847 - уведомление в TechSupport

        /// <summary>
        /// Автоматически отправляет письмо о создании задачи всем сотрудникам, которые находятся в роли, дающей им права автора задачи
        /// </summary>  
        public static void NotifyToAllUsersInTaskAuthorRole(HttpContextBase context, EventTypes eventType, Guid task_id)
        {          
            var isTaskAuthorAHuman = context.Request.Params["task_author"].ToString().ToLower() == Settings.emp_id;
            
            if(isTaskAuthorAHuman == false)
            {
                List<string> emailOwners = new List<string>();
                List<string> taskOwnerRole = new List<string>();
                using (SqlConnection cn = new SqlConnection(Settings.CubeConnection))
                {
                    cn.Open();
                    string empMailSql = @"SELECT emp_mail FROM Employee 
                                WHERE emp_id IN 
                                (SELECT rme_emp_id FROM RoleMapEmployee
                                WHERE rme_role_id = 
                                (SELECT task_owner_role FROM Tasks
                                WHERE task_id = @task_id)
                                )";
                    using (SqlCommand cmd = new SqlCommand(empMailSql, cn))
                    {
                        cmd.Parameters.AddWithValue("task_id", task_id);
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                emailOwners.Add(reader["emp_mail"].ToString());
                            }
                        }
                    }
                    string taskOwnerRoleSql = @"SELECT role_name FROM Roles
                                            WHERE role_id = (
                                            SELECT task_owner_role FROM Tasks
                                            WHERE task_id = @task_id)";
                    using (SqlCommand cmd = new SqlCommand(taskOwnerRoleSql, cn))
                    {
                        cmd.Parameters.AddWithValue("task_id", task_id);
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                taskOwnerRole.Add(reader["role_name"].ToString());
                            }
                        }
                    }
                }

                var task = TaskActions.GetTask(task_id);
                string taskOwnerRoleUrl = String.Format("{0}/view/Roles/{1}#RoleMapEmployee", Settings.UrlTasks, task.task_owner_role.ToString());
                string mess = string.Format(@"
                        <div style=""font-family: Calibri\"">			            
			            <p> Новая задача была создана группой - <a href=""{0}"">{1}</a><br/> 
                        </p>
			            </div>
                        ", taskOwnerRoleUrl, taskOwnerRole[0]);
                foreach (var email in emailOwners)
                {
                    NotifyActions.SetNotify(email, "Support: Автоуведомление", TaskHelper.GetTaskInfoHTML(task_id, mess), task_id);
                }
            }
            
        }

        #endregion


        public static bool CheckChildTaskTypeDuplicate(HttpContextBase context, Guid task_id)
        {
            
                var parentId = Guid.Parse(context.Request.Params["task_parent"]);
                var typeId = Guid.Parse(context.Request.Params["task_type"]);
                var typeList = LinkedTasksActions.GetLinkedTaskTypeGuid(LinkedTasksActions.GetLinkedTaskObject(parentId, task_id, true));
                //return (typeList.Contains(Guid.Parse("4e0e6dfc-7e9e-43a2-b025-67d0993ae376")));
                return (typeList.Contains(typeId));
        }
            
        public static void CheckChildTaskType(HttpContextBase context, EventTypes eventType, Guid task_id)
        {
            if (eventType == EventTypes.beforeCreateTask)
            {
                var parentId = Guid.Parse(context.Request.Params["task_parent"]);

                var typeList = LinkedTasksActions.GetLinkedTaskTypeGuid(LinkedTasksActions.GetLinkedTaskObject(parentId, task_id, true));
                if (typeList.Contains(Guid.Parse("4e0e6dfc-7e9e-43a2-b025-67d0993ae376")))
                {
                    throw new HttpException("Текущий тип задач уже сущетсвует");
                }
            }
        }
        public static void test(HttpContextBase context, EventTypes eventType, Guid task_id)
        {
            if(eventType == EventTypes.beforeCreateTask)
            {
                var parentId = Guid.Parse(context.Request.Params["parent"]);

            }
        }
        //public static void CheckChildTaskType(HttpContextBase context, EventTypes eventType, Guid task_id)
        //{
        //    if(eventType == EventTypes.beforeCreateTask)
        //    {
        //        var parentId = Guid.Parse(context.Request.Params["parent"]);
        //        var typeId = Guid.Parse(context.Request.Params["type"]);

        //        var typeList = LinkedTasksActions.GetLinkedTaskTypeGuid(LinkedTasksActions.GetLinkedTaskObject(parentId, task_id, true ));
        //        if (typeList.Contains(typeId))
        //        {
        //            throw new Exception("Текущий тип задач уже сущетсвует");
        //        }
        //    }
        //}
        /// <summary>
        /// тип задач TEST http://admin.absgroup.ru/view/TaskTypes/0E551C08-A094-4A80-A47D-686DAC82B528
        /// </summary>        
        public static void TestTtype(HttpContextBase context, EventTypes eventType, Guid task_id)
        {
            if (eventType == EventTypes.afterMakeDialog)
            {
                var dummy = String.Empty;
            }
        }

        #region 1с
        public static int CheckEcxistSubTask(Guid? parent_id)
        {
            try
            {
                using (SqlConnection cn = new SqlConnection(Settings.DefaultConnection))
                {
                    cn.Open();
                    String sql = "SELECT count([task_id]) FROM Tasks" +
                                 "WHERE task_parent = @parent_id and" +
                                 "(task_type = '203c09cf-655c-4cd9-aebc-03ccc21c7380' or task_type = 'baf3869e-151b-4b4c-89b7-1710420ada90')";
                    using (SqlCommand cmd = new SqlCommand(sql, cn))
                    {
                        cmd.Parameters.AddWithValue("parent_id", parent_id);
                        cmd.CommandType = CommandType.Text;
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            }
            catch { return 0; }
        }
        public static void UpdateTest1CServer(HttpContextBase context, EventTypes eventType, Guid task_id)
        {
            if (eventType != EventTypes.afterSignStaffDialog)
                return;
            // статус задачи
            var currState = TaskStatesActions.GetTaskSatate(null, task_id);
            //  если не выполнена и не отклонена
            if (currState.tstate_flags != 8)
                return;

            DateTime dt = DateTime.Now;
           
            switch (Convert.ToInt32(dt.DayOfWeek))
            {
                case 1:
                    dt = dt.AddDays(5);
                    break;
                case 2:
                    dt = dt.AddDays(4);
                    break;
                case 3:
                    dt = dt.AddDays(3);
                    break;
                case 4:
                    if (dt.Hour >= 12)
                    {
                        dt = dt.AddDays(9);
                    }
                    else { dt = dt.AddDays(2); }
                    break;
                case 5:
                    dt = dt.AddDays(8);
                    break;
                case 6:
                    dt = dt.AddDays(7);
                    break;


            }
            using (SqlConnection cn = new SqlConnection(Settings.CubeConnection))
            {
                cn.Open();
                string sql = @"UPDATE [dbo].[TaskParams]
                               SET tpar_value = @dt
                               WHERE [tpar_template] = '878B3ADB-9C60-4001-9864-4F55AD1B0AF7' and [tpar_task_id] = @task_id";
                using (SqlCommand cmd = new SqlCommand(sql, cn))
                {
                    cmd.Parameters.AddWithValue("dt", dt.ToLongDateString());
                    cmd.Parameters.AddWithValue("task_id", task_id);
                    cmd.ExecuteNonQuery();
                }
            }
        }
        public static void CreateWork1CServer(HttpContextBase context, EventTypes eventType, Guid task_id)
        {
            if (eventType != EventTypes.afterMakeDialog) return;
            using (var db = new EF.SupportWebEntities())
            {
                Guid? parent_task_id = db.Tasks.Where(t => t.task_id == task_id).Single().task_parent;
                if (parent_task_id != null)
                {
                    var task_name = db.Tasks.Where(t => t.task_id == parent_task_id).Single().task_name;
                    var sub_task = db.Tasks.Where(t => t.task_parent == parent_task_id && t.fl_deleted == null ).Select(t => t.task_flag).ToList();
                    if (sub_task.Count == 2 && sub_task.Where(t => t == -2147483648 || t == 128).Select(t => t).ToList().Count() == 2)
                    {
                            
                            var ttypes = new List<Guid>();
                            ttypes.Add(Guid.Parse("c54e33f2-dabe-42aa-8f2f-20d5114ebfd2")); //ДИТР. Обновление платформы на рабочих серверах
                            ttypes.Add(Guid.Parse("3525b7e3-b7bb-4418-822d-2edc7f503731")); //ДИТР. Обновление платформы на рабочих РМ

                        using (SqlConnection cn = new SqlConnection(Settings.CubeConnection))
                            {
                                cn.Open();

                            foreach (var ttype in ttypes)
                            {
                                using (SqlCommand cmd = new SqlCommand("dbo.cpCreateTask", cn))
                                {
                                    cmd.CommandType = CommandType.StoredProcedure;

                                    cmd.Parameters.AddWithValue("@emp_id", Settings.emp_id);
                                    cmd.Parameters.AddWithValue("@task_parent", parent_task_id);
                                    cmd.Parameters.AddWithValue("@task_type", ttype);
                                    cmd.Parameters.AddWithValue("@task_name", task_name);

                                    cmd.ExecuteNonQuery();

                                }
                            }
                           
                        }

                    }
                }
            }
        }
        #endregion
        #region podbor
        /// <summary>
        /// тип задач ДРП. Заявка на подбор персонала http://admin.absgroup.ru/view/TaskTypes/b5ae5d08-6224-462a-8a86-3d1c400accec
        /// TODO: AsNoTracking
        /// Polly
        /// 
        /// </summary>        
        public static void Recruitmenttype(HttpContextBase context, EventTypes eventType, Guid task_id)
        {
            //return;

            // код для лога
            const string objectName = "huntflow-sync";

            var state = TaskStatesActions.GetTaskSatate(null, task_id);
            // если не на утверждении и не отклонена
            if (state.tstate_flags != 2 && state.tstate_flags != 16)
            {
                using (var db = new EF.SupportWebEntities())
                {
                   DateTime dt = new DateTime(2022, 01, 31);
                    var created = db.Tasks.SingleOrDefault(t => t.task_id == task_id).fl_created;
                    var TaskEcxist = db.HuntflowTasks.FirstOrDefault(hf => hf.hf_task_id == task_id);
                    if (created < dt) return;

                    var dbTask = db.Tasks.Single(t => t.task_id == task_id);
                    var pars = db.TaskParams
                         .AsNoTracking()
                         .Where(tp => tp.tpar_task_id == task_id)
                         .OrderBy(tp => tp.tpar_order)
                         .ThenBy(tp => tp.tpar_name)
                         .ToList()
                         .Select(tp => new Param
                         {
                             Id = tp.tpar_id,
                             Name = tp.tpar_name,
                             Value = String.IsNullOrWhiteSpace(tp.tpar_value) ? tp.tpar_text_value : tp.tpar_value,
                             Required = tp.tpar_required == "1" ? true : false,
                             Template = tp.tpar_template
                         });
                   
                    var task = new Task()
                    {
                        Id = dbTask.task_id,
                        Number = dbTask.task_number,
                        Title = dbTask.task_name,
                        Description = dbTask.task_desc,
                        OrgName = dbTask.task_org == null ? string.Empty : dbTask.Orgs.org_name,
                        TypeName = dbTask.TaskTypes.ttype_name,
                        Start = dbTask.task_start,
                        End = dbTask.task_deadline,
                        Pars = new List<Param>().Concat(pars).Where(item => item.Key != null).ToList()
                    };

                    var json = JsonConvert.SerializeObject(task);
                    if (TaskEcxist == null)
                    {
                        SyncData(task_id, json, RequestHelper.MethodType.Post, objectName, "CREATE");
                    }
                   
                }
            }
        }

       
        public static void SyncData(Guid task_id, string json, RequestHelper.MethodType methodType, string logObjName, string logType)
        {
            // отправляем задачу
            var request = new RequestHelper();
            var result = request.Request(
                methodType == RequestHelper.MethodType.Post ? ConfigurationManager.AppSettings["PortalHuntflowUrlVacanciesPOST"]
                                                            : ConfigurationManager.AppSettings["PortalHuntflowUrlVacanciesPATCH"].Replace("{id}", task_id.ToString())
                , json
                , methodType);

            
            // hf
            JavaScriptSerializer js = new JavaScriptSerializer();
            HuntflowTask huntflowTask = js.Deserialize<HuntflowTask>(result.Result);
            HuntflowActions.SetHFTask(huntflowTask.taskId, huntflowTask.vacancyId, result.Result);
            // log
            EventLogsActions.AddEventLogs(Guid.Parse(Settings.emp_id), logObjName, task_id, "CRAETE", null);

        }

        public class HuntflowTask
        {
           public  Guid taskId { get; set; }
           public int vacancyId { get; set; }
        }

        // http://localhost:11116/CubeTask/a565399d-2379-40bc-93e5-31da4ae093dd
        // http://localhost:11116/task/edit/d9e4b641-279d-4593-a426-25f994553771
        public class Param
        {
            [JsonProperty("id")]
            public Guid Id;

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("value")]
            public string Value { get; set; }

            [JsonProperty("key")]
            public int? Key
            {
                get
                {
                    // TODO переделай чз админку
                    if (Guid.Parse("6f9c953a-3fd0-4704-830e-d6253062264b") == Template)
                    {
                        return 10;
                    }
                    else if (Guid.Parse("F4E9959E-276B-4FDD-833E-B610AEB06694") == Template)
                    {
                        return 20;
                    }
                    else if (Guid.Parse("19461CF8-5233-4445-B278-3C654095820F") == Template)
                    {
                        return 30;
                    }
                    else if (Guid.Parse("E284FC86-30C2-4390-8D05-00F8D5B4EDC6") == Template)
                    {
                        return 40;
                    }
                    else if (Guid.Parse("FDD25456-6223-4215-B64E-B2E453F0A827") == Template)
                    {
                        return 50;
                    }
                    else if (Guid.Parse("6C81419D-55B2-42CB-BDE9-C5FC07D78E40") == Template)
                    {
                        return 60;
                    }
                    else if (Guid.Parse("ABCFD44F-3F45-4506-9BD0-CC1F67C3D013") == Template)
                    {
                        return 70;
                    }
                    else if (Guid.Parse("2B5D8941-B6B9-4DB0-B193-27A052A3CD25") == Template)
                    {
                        return 80;
                    }
                    else if (Guid.Parse("515679C2-FA8F-4FD0-9327-DDB4380329C1") == Template)
                    {
                        return 90;
                    }
                    else if (Guid.Parse("7F388A9C-19CD-4C7D-8105-DC51CD367104") == Template)
                    {
                        return 100;
                    }
                    else if (Guid.Parse("1FE61101-C460-486F-964D-47E47321189A") == Template)
                    {
                        return 110;
                    }
                    else if (Guid.Parse("6FC163BD-9EFC-40BA-A9B3-853CE61FDAA8") == Template)
                    {
                        return 120;
                    }
                    else if (Guid.Parse("66EA3609-6B0C-4B0F-B76D-AD7F9409C0FB") == Template)
                    {
                        return 127;
                    }
                    else if (Guid.Parse("48D8480F-E859-4F57-B9A8-55BE6FF9475B") == Template)
                    {
                        return 130;
                    }
                    else if (Guid.Parse("0559DA1E-62E5-4238-8075-79B6AB7D8A8A") == Template)
                    {
                        return 140;
                    }
                    else if (Guid.Parse("458A95E3-8426-433B-B61D-1D750070AA59") == Template)
                    {
                        return 150;
                    }
                    else if (Guid.Parse("8EBB5024-E5CE-447D-842D-5B8C79B0D893") == Template)
                    {
                        return 160;
                    }
                    else if (Guid.Parse("67C8843A-EB38-4C5C-9797-C480F73C8F4E") == Template)
                    {
                        return 170;
                    }
                    else if (Guid.Parse("A5B62DCC-8694-4661-8EC0-CA42D39B3270") == Template)
                    {
                        return 180;
                    }
                    else if (Guid.Parse("4AC66381-5C2F-4DAC-B0F4-6149503B7200") == Template)
                    {
                        return 190;
                    }
                    else if (Guid.Parse("3BC45BFB-10F4-4F27-8A25-C6FE0C1D96B6") == Template)
                    {
                        return 200;
                    }
                    else if (Guid.Parse("B9AE260A-1C6B-4FA6-8EC8-54866B53799D") == Template)
                    {
                        return 210;
                    }
                    else if (Guid.Parse("1FA97B80-D78F-4B8E-AA7C-D048DDDD2B9C") == Template)
                    {
                        return 220;
                    }
                    else if (Guid.Parse("1D82D156-44FA-4361-96CB-3B6B68BCE55D") == Template)
                    {
                        return 230;
                    }
                    else if (Guid.Parse("DB407ED8-D532-465C-8913-E799A97322F6") == Template)
                    {
                        return 240;
                    }
                    else if (Guid.Parse("AF7640FD-55FA-4EB9-8325-A1146E787E74") == Template)
                    {
                        return 250;
                    }
                    else if (Guid.Parse("3EEA87B7-DF2B-474F-95B7-3ED413461D6D") == Template)
                    {
                        return 260;
                    }
                    else if (Guid.Parse("A6E8C116-985C-4D95-9D8B-E85E02459ABC") == Template)
                    {
                        return 270;
                    }
                    else if (Guid.Parse("059DDAF4-B294-44C5-B066-D2C8C3006605") == Template)
                    {
                        return 280;
                    }
                    else if (Guid.Parse("b6fa6913-37f1-4841-8ad7-490ab30cc320") == Template)
                    {
                        return 290;
                    }
                    else if (Guid.Parse("20b06ed9-e4e2-49ef-8ea7-fe0dc850a640") == Template)
                    {
                        return 300;
                    }

                    else return null;
                }
            }

            [JsonProperty("required")]
            public bool Required { get; set; }

            [JsonIgnore()]
            public Guid? Template;
        }

        public class FileItem
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("size")]
            public long Size { get; set; }
        }

        public class Task
        {
            [JsonProperty("id")]
            public Guid Id;

            [JsonProperty("number")]
            public int Number { get; set; }

            [JsonProperty("orgName")]
            public string OrgName { get; set; }

            [JsonProperty("typeName")]
            public string TypeName { get; set; }

            [JsonProperty("title")]
            public string Title { get; set; }

            [JsonProperty("description")]
            public string Description { get; set; }

            [JsonProperty("start")]
            public DateTime? Start { get; set; }

            [JsonProperty("end")]
            public DateTime? End { get; set; }

            [JsonProperty("pars")]
            public List<Param> Pars { get; set; }

            [JsonProperty("files")]
            public List<FileItem> Files { get; set; }
        }

        #endregion
    }
}
