function initializeActivityMonitor() {
    var sse = new EventSource('/api/sse/activity', { withCredentials: true });
    sse.addEventListener('globalactivity', function (data) {
        console.log('globalactivity', data);
    }, false);

    sse.onmessage = function (event) {
        console.log('message: ', event);
    }

    sse.onopen = function (event) {
        console.log('SSE connection opened');
    }
}

(function () {
    initializeActivityMonitor();
})();