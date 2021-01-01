function initializeActivityMonitor() {
    var sse = new EventSource('/api/sse/activity', { withCredentials: true });
    sse.addEventListener('globalactivity', function (data) {
        var _data = JSON.parse(data.data);
        var json = JSON.stringify(_data);

        console.log('globalactivity', _data);
        createToast('Global activity', json);
    }, false);

    sse.addEventListener('useractivity', function (data) {
        var _data = JSON.parse(data.data);
        var json = JSON.stringify(_data);

        console.log('useractivity', _data);
        createToast('User activity', json);
    }, false);

    sse.onmessage = function (event) {
        var _data = JSON.parse(event.data);
        var json = JSON.stringify(_data);

        console.log('message: ', _data);
        createToast('Message', json);
    }

    sse.onopen = function (event) {
        console.log('SSE connection opened');
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