function getTaskBlock(element) {
    //while (element && element.id == '') element = element.parentNode;
    element = $(element).closest('.absTaskBlock')[0];

    return element;
}

function showPopupForm(element, url, width) {
    var task = getTaskBlock(element);
    var data = {
        id: task.id,
        full: $(task).data('full'),
    };
    var title = $(task).attr('title');
    var popup = createPopupWindow(task, title, null, width);
    popup.attachURL('/task/' + url + '?' + $.param(data));
}

function onTaskUpdate(task_id, full) {
    var div = document.getElementById(task_id);
    if (full === undefined) full = $(div).data('full')
    $.ajax({
        url: '/task/' + task_id,
        data: { full: full },
        success: function (data) {
            $(data).filter('div').each(function () {
                if (this.id === task_id) {
                    div.outerHTML = this.outerHTML;
                }
            });
            updateCounters();
            onInitPage();
        },
        error: onAjaxError,
    });
}

function onTaskMessage(task_id, message) {
    dhtmlx.message(message);
    onTaskUpdate(task_id);
}

function onTaskError(message) {
    dhtmlx.message({ type: 'error', text: message });
}

function copyNumber(element) {
    if (navigator.userAgent.indexOf("MSIE") >= 0 && window.clipboardData) {
        window.clipboardData.setData('text', '' + $(element).data('number'));
    }
}

function onTaskMore(e) {
    onTaskUpdate(getTaskBlock(e).id, $(e).data('full') == 1 ? 0 : 1);
}
// подтвердить/отклонить, у автора
function showConfirmPopup(button) { showPopupForm(button, 'Confirm', 450); }
function showRejectPopup(button) { showPopupForm(button, 'Reject', 450); }

function showMakePopup(button) { showPopupForm(button, 'Make'); }
function showSignPopup(button) { showPopupForm(button, 'Sign'); }
function showNotePopup(button) { showPopupForm(button, 'Note', 450); }
function showSignStaff(button) {
    showPopupForm(button, 'SignStaff', 600);
}
function showTaskStaff(button) { showPopupForm(button, 'TaskStaff', 670); }
function showTaskWatchersPopup(button) { showPopupForm(button, 'TaskWatchers', 570); }

function showSubTaskPopup(button) {
    var task = getTaskBlock(button);
    $.ajax({
        url: '/task/SubTasks.aspx',
        data: { id: task.id },
        success: function (data) {
            if ($.trim(data) == '') {
                location = '/task/edit?parent=' + task.id;
            } else {
                var title = $(task).attr('title');
                var popup = createPopupWindow(task, title);
                popup.attachHTMLString(data);
                popup.setDimension(600, 600);
            }
        },
        error: onAjaxError,        
    });
}

// создать задачу в Жира
function onImportJiraClick(e) {
    var task_id = $(e).attr('data-id');
    $.ajax({
        url: '/task/SyncJiraTaskPortal',
        type: 'POST',
        data: { task_id: task_id },
        success: function (data) {
            dhtmlx.message('Задача создана');
        },
        error: onAjaxError,
        complete: function () {
            setTimeout(function () {
                window.location.reload();
            }, 2 * 1000);
        }
    });
}

function createSubTasks(button, task_id) {
    $('input.SubTaskType').each(function () {
        if ($(this).val() == '1') {
            var data = { parent: task_id, type: this.id };
            window.parent.open('/task/edit?' + $.param(data), '_blank');
        }
    });
}

function onInitPage() {
    if (!('zeroclipboard' in window)) {
        ZeroClipboard.config({
            swfPath: '/3rd/zeroclipboard/ZeroClipboard.swf',
            title: 'Скопировать номер задачи',
            forceHandCursor: true,
        });
        zeroclipboard = new ZeroClipboard();
    }
    zeroclipboard.clip($('.zeroclipboard').removeClass('zeroclipboard'));
    initLyncPresencePopup();
    initHints();
    initLikes();
    calcEmployeeCounters();
}

function rank(e) {
    var parent = $(e).parent();
    if (parent.hasClass('active')) {
        var firstRate = parent.children('.rated').length > 0 ? false : true;// если не первый раз оцениваем эту задачу, то счетчики можно не обновлять
        var list = parent.children();
        $.post(
            '/root/exec/cpSetTaskRank',
            { task_id: parent.data('task'), task_rank: list.index(e) + 1 },
            function (html) {
                var rank = parseInt(html);
                list.each(function (index) {
                    if (index < rank) {
                        $(this).addClass('rated');
                    } else {
                        $(this).removeClass('rated');
                    }
                    if (firstRate) updateCounters();
                });
            }
        );
    }
}




$(document).ready(function () {
    dhxWins = new dhtmlXWindows();
    dhxWins.attachEvent("onContentLoaded", doResizeDlg);




});