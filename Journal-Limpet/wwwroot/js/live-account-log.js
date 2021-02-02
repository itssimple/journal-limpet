function initializeLogMonitor() {
    var sse = new EventSource('/api/sse/liveuserlog', { withCredentials: true });
    sse.addEventListener('userlog', createLogLine, false);

    sse.onerror = function (e) {
        sse.removeEventListener('userlog', createLogLine, false);
        sse.close();
        setTimeout(initializeActivityMonitor, 5000);
    }

    function tryJsonParse(data) {
        try {
            return JSON.parse(data);
        } catch {
            return data;
        }
    }

    const maxLines = 100;

    function createLogLine(data) {
        let fragment = document.createDocumentFragment();

        let line = document.createElement('div');
        line.className = 'terminal-line';
        line.innerText = `${new Date().toISOString()} :: ${JSON.stringify(tryJsonParse(data.data), null, 2)}`;

        fragment.appendChild(line);

        let containerItems = document.querySelectorAll('.terminal-line');

        while (containerItems.length >= maxLines) {
            containerItems[0].remove();
            containerItems = document.querySelectorAll('.terminal-line');
        }

        let container = document.querySelector('.log-window');

        container.appendChild(fragment);

        line.scrollIntoView();

        return fragment;
    }
}

(function () {
    initializeLogMonitor();
})();