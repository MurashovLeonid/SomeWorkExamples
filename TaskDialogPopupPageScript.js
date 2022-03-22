$(function () {
    // счетчики задач по сотрудникам
    calcEmployeeCounters();
   
    $('input[type=radio][name=task_state]').change(function () {

        var requiredComment = $(this).attr('data-required-comment');
        var taskFlag = $(this).attr('data-flags')

        if (requiredComment == 1 || taskFlag == 16 ) {
            $('textarea[name = text][class = "absTextArea"]').attr('data-required', 'true');
        }
        else {
            $('textarea[name = text][class = "absTextArea"]').attr('data-required', 'false');
        }
    });
})

function createCombo() {
    $('select[data-combo]').each(function () {
        var obj = $(this).data('combo');
        var name = $(this).attr('name');
        var combo = dhtmlXComboFromSelect(this);
        document.combobox[name] = combo;
        document.combobox.all[document.combobox.all.length] = combo;
        var url = '/root/combo/' + obj;
        combo.enableFilteringMode('between', url);
        combo.allowFreeText(false);
        var resize = function () {
            var w = $(combo.DOMelem).width();
            $(combo.DOMelem_input).width(w - 24);
            combo.setOptionWidth(w);
        };
        $(window).resize(resize);
        resize();
    });
}

function switchCheckbox(element) {
    var ch = $(element).parent().children().get(0);
    ch.checked = !ch.checked;
}

function onCheckBox(element) {
    var x = $(element).prev();
    x.val(x.val() == '1' ? '0' : '1');
}

function onTaskState(element) {
    if ($(element).data('flags') == '-2147483648') {
        $('#date').val($('#now').val());
    } else {
        $('#date').val('');
    }

}

function onCheckCommentBeforeSetMake(value) {

    var checkcomment = false;
    var tstate_id;
    if (value == undefined) {
        tstate_id = $('input[name = "task_state"]:checked').val();
    }
    else {
        tstate_id = value;
    }
 
    $.ajax({
        method: 'POST',
        url: '/Task/CheckCommentBeforeSetMake',
        async: false,
        data: {
            task_state_id: tstate_id,
        },
        success: function (data) {
            checkcomment = data.checkrequiredcomment;
            
        },
    });
    return checkcomment;
}

 

function SubmitMake() {
    debugger;

    //var cancel = false;
    //$('input[data-flags="16"]').each(function () {
    //    if (this.checked) cancel = true;
    //});
    //if (!cancel && $('input[name="task_state"]').attr('type') == 'hidden') {
    //    cancel = true;
    //}
    //if (cancel && $.trim($('textarea').val()) === '') {
    //    var text = 'Сформулируйте причину отказа!';
    //    parent.dhtmlx.message({ type: 'error', text: text });
    //    return;      
    //}
    var $statusRadio = $('input[type="radio"][name="task_state"]:checked');
    if (($statusRadio.attr('data-required-comment') == 1 || $statusRadio.attr("data-flags") == 16) && $.trim($('textarea').val()) === '') {
        var text = 'Пожалуйста, заполните поле для комментариев!';
        parent.dhtmlx.message({ type: 'error', text: text });
        return;
    }
    

    DoSubmit();
}
// на доработку
function SubmitReject() {
    debugger;
    if ($.trim($('textarea').val()) === '') {
        var text = 'Сформулируйте причину отказа!';
        parent.dhtmlx.message({ type: 'error', text: text });
        return;
    }
    
    DoSubmit();
}

function SubmitSign(accept) {
    debugger;
    var empty = true;
    $('input[type=checkbox]').each(function () {
        if (this.checked) empty = false;
    });
    if (empty) {
        var text = 'Не выбрана роль для подписи!';
        parent.dhtmlx.message({ type: 'error', text: text });
        return;
    }
    if (accept === 0 && $.trim($('textarea').val()) === '') {
        var text = 'Сформулируйте причину отказа!';
        parent.dhtmlx.message({ type: 'error', text: text });
        return;
    }
    $('input[name=sign_accept]').attr('value', accept);
    DoSubmit();
}

function DoSubmit() {
    debugger;
    $('form').submit().hide();
    $('#progress').show();
}

function onAppendSignStaff() {
    var combo = document.combobox.RoleCombo;
    var role_id = combo.curr_id;

    if (role_id == null || role_id == '') {
        dhtmlx.message('Выберите роль!');
        return;
    }
    
    if (document.getElementById(role_id) !== null) {
        var text = combo.curr_text;
        dhtmlx.message(text + '<br>&mdash; роль уже имеется в списке.');
        return;
    }
    $.get('/task/SignItem?id=' + role_id, function (html) {
        $('#SignStaffData').append(html);
        parent.autosize();
    });
}

function onDeleteSignStaff(element) {
    var table = document.getElementById('SignStaffData');
    var row = element.parentNode.parentNode;
    if (row.id !== '') {
        var form = table.parentNode;
        var input = document.createElement('input');
        input.type = 'hidden';
        input.name = 'delete';
        input.value = row.id;
        form.appendChild(input);
    }
    row.outerHTML = '';
    parent.autosize();
}

function onAppendTaskStaff() {
    var combo = document.combobox.EmployeeCombo;
    var value = combo.getActualValue();
    if (value === '') return;
    combo.setComboText('');
    combo.setComboValue('');
    combo.DOMelem_input.value = '';
    combo.DOMelem_input.focus();

    var element = document.getElementById(value.toUpperCase());
    if (element !== null) {
        var text = combo.getSelectedText();
        parent.dhtmlx.message(text + '<br>&mdash; сотрудник уже имеется в списке.');
        $('[type="checkbox"][name="work"][value="' + value.toUpperCase() + '"]').prop('checked', true);
        return;
    }

    $.get('/task/StaffItem?id=' + value, function (html) {
        $('#TaskStaffData').append(html);
        // счетчики задач по сотрудникам
        calcEmployeeCounters();
        parent.autosize();
    });
}

function onDeleteTaskStaff(element) {
    var row = element.parentNode;
    row.parentNode.removeChild(row);
    $('<input>').attr({
        type: 'hidden',
        name: 'staff',
        value: row.id,
    }).appendTo('form');
    $.post('/root/exec/cpDeleteStaffTemplate', {
        task_type: $('#task_type').val(),
        emp_id: row.id,
    });
}

function onAppendTaskWatcher() {
    var combo = document.combobox.EmployeeCombo;
    var value = combo.getActualValue();
    if (value === '') return;
    combo.setComboText('');
    combo.setComboValue('');
    combo.DOMelem_input.value = '';
    combo.DOMelem_input.focus();

    var element = document.getElementById(value.toUpperCase());
    if (element !== null) {
        var text = combo.getSelectedText();
        parent.dhtmlx.message(text + '<br>&mdash; сотрудник уже имеется в списке.');
        $('[type="checkbox"][name="work"][value="' + value.toUpperCase() + '"]').prop('checked', true);
        return;
    }

    $.get('/task/WatcherItem?id=' + value, function (html) {
        $('#TaskWatcherData').append(html);
        parent.autosize();
    });
}

function onCheckTaskStaff(e) {
    $('input[name=work]').prop('checked', e.checked);
}

function onCheckTaskWatcher(e) {
    $('input[name=selected]').prop('checked', e.checked);
}

function SaveTemplate(btn) {
    var $tarea = $(btn).parent().next();
    var text = $.trim($tarea.val());
    if (text !== '') {
        $.ajax({
              type: "POST"
            , url: "/Task/ManageTemplate"
            , data: { procName: "cpAddExecTemplate", text: text }
            , success: function (responce) {
                if (responce) {
                    dhtmlx.message({ type: "messageCss", text: "Шаблон успешно сохранён." });
                }
                else {
                    dhtmlx.message({ type: "errorCss", text: "Не удалось сохранить шаблон." });
                }
            }
        });
    }
    else {
        dhtmlx.message({ type: "warningCss", text: "Введите текст описания для создания шаблона." });
    }
}

function ManageTemplate(btn) {    
    if (!('dhxDlgWins' in window)) {
        dhxDlgWins = new dhtmlXWindows();
    }
    dhxDlgWins.forEachWindow(function (win) { win.close(); });    
    var w = $(window).width() - 12;
    var h = $(window).height() - 30;    
    var popup = dhxDlgWins.createWindow({ id: "DlgTemplates", left: (($(window).width() - w) / 2), top: (($(window).height() - h) / 2), width: w, height: h, modal: true, move: false });
    popup.setText("Шаблоны описаний выполнения");
    popup.button('minmax1').hide();
    popup.button('minmax2').hide();
    popup.button('park').hide();

    var $templates = $("<div></div>");

    $.ajax({
          type: "POST"
        , url: "/Task/GetTemplates"
        , data: { procName: "cpGetExecTemplate" }
        , success: function (responce) {
            if ($(responce).find('.tbl_templates').length) {
                $templates.append($(responce));
                var $content = $templates.find("#content");
                var $table = $content.find('.tbl_templates');
                var $tools = $templates.find('.absPopupFoot');

                $content.css("width", (w - 34));
                $content.css("height", (h - 125));

                $tools.on('click', '.absDialogBtn', function () {
                    var $btn = $(this);
                    if ($btn.text() === "Выбрать") {                        
                        var $radio = $table.find('input[type="radio"]:checked');
                        if ($radio.length) {                           
                            var $tarea = $(btn).parent().next();
                            $tarea.val($radio.closest('tr').text())
                        }
                        else
                        {
                            dhtmlx.message({ type: "warningCss", text: "Шаблон не выбран." });
                            return;
                        }
                    }
                    popup.close();
                });
                $table.on('click', 'tr', function () {
                    $(this).find('input[type="radio"]').get(0).click();
                });
                $table.on('click', 'input[type="image"].delete', function () {
                    var $btnI = $(this);
                    dhtmlx.confirm({
                        ok: "Да",
                        cancel: "Отмена",
                        type: "confirmCss",
                        text: "Удалить шаблон?",
                        callback: function (result) {
                            if (result) {                               
                                $.ajax({
                                    type: "POST"
                                    , url: "/Task/ManageTemplate"
                                    , data: { procName: "cpDeleteExecTemplate", id: $btnI.data('templateid') }
                                    , success: function (responce) {                                       
                                        if (responce) {
                                            dhtmlx.message({ type: "messageCss", text: "Шаблон успешно удалён." });
                                            $btnI.closest('tr').remove();
                                        }
                                        else {
                                            dhtmlx.message({ type: "errorCss", text: "Не удалось удалить шаблон." });
                                        }
                                    }
                                });
                            }
                        }
                    });
                });

                popup.attachObject($templates.get(0));
            }
        }
    });

    
}

$(document).ready(function () {
    document.combobox = { all: [] };
    createCombo();

    $('input[placeholder]').each(function () {
        $(this).placeholder({ customClass: 'input-placeholder' });
    });
})
