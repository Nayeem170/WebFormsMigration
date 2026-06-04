let _confirmCallback = null;

function showConfirm(msg, callback) {
    document.getElementById('confirmMsg').textContent = msg;
    document.getElementById('confirmOverlay').classList.add('open');
    _confirmCallback = callback;
}

function closeConfirm() {
    document.getElementById('confirmOverlay').classList.remove('open');
    _confirmCallback = null;
}

document.addEventListener('DOMContentLoaded', function () {
    document.getElementById('confirmYes').onclick = function () {
        if (_confirmCallback) _confirmCallback();
        closeConfirm();
    };
});
