function initializeActivityMonitor() {
    var sse = new EventSource('/api/sse/activity', { withCredentials: true });
    sse.addEventListener('globalactivity', popMessage, false);
    sse.addEventListener('useractivity', popMessage, false);

    sse.onerror = function (e) {
        if (sse.readyState === 2) {
            sse.removeEventListener('globalactivity', popMessage, false);
            sse.removeEventListener('useractivity', popMessage, false);
            setTimeout(initializeActivityMonitor, 5000);
        }
    }

    function tryJsonParse(data) {
        try {
            return JSON.parse(data);
        } catch {
            return false;
        }
    }

    function popMessage(data) {
        var _data = tryJsonParse(data.data);
        if (_data) {
            createToast(_data.title, _data.message, _data.class);
        }
    }
}

function createToast(title, message, spinnerClass) {
    if (!spinnerClass) spinnerClass = 'warning';

    var toast = $(`<div class="toast">
    <div class="toast-header">
        <div class="spinner-border text-${spinnerClass} spinner-border-sm" role="status">
            <span class="sr-only">Activity...</span>
        </div>
        &nbsp;&nbsp;
        <strong class="mr-auto">${title}</strong>
        <button type="button" class="ml-2 mb-1 close" data-dismiss="toast" aria-label="Close">
            <span aria-hidden="true">&times;</span>
        </button>
    </div>
    <div class="toast-body">
        ${message}
    </div>
</div>`);

    $('.journal-limpet.toast-holder').append(toast);

    toast.toast({
        autohide: true,
        delay: 3000
    }).toast('show')
}

(function () {
    initializeActivityMonitor();
})();