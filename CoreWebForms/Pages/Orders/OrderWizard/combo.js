function initCombo(id) {
    var wrap = document.getElementById(id);
    if (!wrap) return;
    var input = wrap.querySelector('.combo-input');
    var sel = wrap.querySelector('select');
    var list = wrap.querySelector('.combo-list');
    if (!input || !sel || !list) return;

    wrap._activeIndex = -1;
    wrap._allOpts = [];
    for (var i = 0; i < sel.options.length; i++) {
        var v = sel.options[i].value;
        var t = sel.options[i].text;
        if (v === '' || v === t || t.indexOf('--') === 0) continue;
        wrap._allOpts.push({ text: t, value: v });
    }

    input.value = '';

    input.oninput = function () { filterCombo(id); };
    input.onfocus = function () { openCombo(id); };
    input.onblur = function () { setTimeout(function () { closeCombo(id); }, 200); };
    input.onkeydown = function (e) { comboKeydown(e, id); };

    wrap.querySelector('.combo-arrow').onclick = function () { toggleCombo(id); };
}

function populateCombo(id) {
    var wrap = document.getElementById(id);
    if (!wrap) return;
    var list = wrap.querySelector('.combo-list');
    var input = wrap.querySelector('.combo-input');
    if (!wrap._allOpts || !list) return;

    var term = (input ? input.value : '').toLowerCase();
    list.innerHTML = '';

    for (var i = 0; i < wrap._allOpts.length; i++) {
        if (wrap._allOpts[i].value === '') continue;
        if (wrap._allOpts[i].text.toLowerCase().indexOf(term) !== -1) {
            var div = document.createElement('div');
            div.className = 'combo-item';
            div.textContent = wrap._allOpts[i].text;
            div.setAttribute('data-value', wrap._allOpts[i].value);
            (function (val, txt) {
                div.onmousedown = function (e) {
                    e.preventDefault();
                    selectComboItem(id, val, txt);
                };
            })(wrap._allOpts[i].value, wrap._allOpts[i].text);
            list.appendChild(div);
        }
    }

    if (list.children.length === 0) {
        var empty = document.createElement('div');
        empty.className = 'combo-empty';
        empty.textContent = 'No products found';
        list.appendChild(empty);
    }
}

function openCombo(id) {
    var wrap = document.getElementById(id);
    if (!wrap) return;
    populateCombo(id);
    var list = wrap.querySelector('.combo-list');
    if (list) list.classList.add('open');
    wrap._activeIndex = -1;
}

function closeCombo(id) {
    var wrap = document.getElementById(id);
    if (!wrap) return;
    var list = wrap.querySelector('.combo-list');
    if (list) list.classList.remove('open');
    wrap._activeIndex = -1;
}

function toggleCombo(id) {
    var wrap = document.getElementById(id);
    if (!wrap) return;
    var list = wrap.querySelector('.combo-list');
    if (!list) return;
    if (list.classList.contains('open')) closeCombo(id);
    else { var input = wrap.querySelector('.combo-input'); if (input) input.focus(); }
}

function filterCombo(id) {
    var wrap = document.getElementById(id);
    if (!wrap) return;
    var list = wrap.querySelector('.combo-list');
    populateCombo(id);
    if (list && !list.classList.contains('open')) list.classList.add('open');
}

function selectComboItem(id, value, text) {
    var wrap = document.getElementById(id);
    if (!wrap) return;
    var input = wrap.querySelector('.combo-input');
    var sel = wrap.querySelector('select');
    if (!input || !sel) return;

    input.value = text;
    closeCombo(id);

    for (var i = 0; i < sel.options.length; i++) {
        if (sel.options[i].value === value) {
            sel.selectedIndex = i;
            return;
        }
    }
}

function comboKeydown(e, id) {
    var wrap = document.getElementById(id);
    if (!wrap) return;
    var list = wrap.querySelector('.combo-list');
    if (!list) return;
    var items = list.querySelectorAll('.combo-item');

    if (e.key === 'ArrowDown') {
        e.preventDefault();
        if (!list.classList.contains('open')) { openCombo(id); return; }
        if (items.length === 0) return;
        wrap._activeIndex = Math.min(wrap._activeIndex + 1, items.length - 1);
        setHighlight(items, wrap);
    } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        if (!list.classList.contains('open') || items.length === 0) return;
        wrap._activeIndex = Math.max(wrap._activeIndex - 1, 0);
        setHighlight(items, wrap);
    } else if (e.key === 'Enter' && wrap._activeIndex >= 0 && items[wrap._activeIndex]) {
        e.preventDefault();
        var val = items[wrap._activeIndex].getAttribute('data-value');
        var txt = items[wrap._activeIndex].textContent;
        selectComboItem(id, val, txt);
    } else if (e.key === 'Escape') {
        closeCombo(id);
        var escInput = wrap.querySelector('.combo-input');
        if (escInput) escInput.blur();
    }
}

function setHighlight(items, wrap) {
    for (var i = 0; i < items.length; i++) {
        if (i === wrap._activeIndex) {
            items[i].classList.add('active');
            items[i].scrollIntoView({ block: 'nearest' });
        } else {
            items[i].classList.remove('active');
        }
    }
}
