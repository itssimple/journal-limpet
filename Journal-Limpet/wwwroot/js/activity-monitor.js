function initializeActivityMonitor() {
    var sse = new EventSource('/api/sse/activity', { withCredentials: true });
    sse.addEventListener('globalactivity', popMessage, false);
    sse.addEventListener('useractivity', popMessage, false);
    sse.addEventListener('statsactivity', updateStats, false);

    sse.onerror = function (e) {
        sse.removeEventListener('globalactivity', popMessage, false);
        sse.removeEventListener('useractivity', popMessage, false);
        sse.removeEventListener('statsactivity', updateStats, false);
        sse.close();
        setTimeout(initializeActivityMonitor, 5000);
    }

    function tryJsonParse(data) {
        try {
            return JSON.parse(data);
        } catch {
            return false;
        }
    }

    function popMessage(data) {
        let _data = tryJsonParse(data.data);
        if (_data) {
            createToast(_data.title, _data.message, _data.class);
        }
    }

    function updateStats(data) {
        let _data = tryJsonParse(data.data);
        let statsAvailableOnPage = document.querySelectorAll('.index-stats');
        if (statsAvailableOnPage.length > 0) {
            let userCount = document.querySelector('#stat-usercount');

            if (_data.TotalUserCount) {
                userCount.innerHTML = `${_data.TotalUserCount.toLocaleString()} users registered`;
            }

            let journalCount = document.querySelector('#stat-journalcount');

            if (_data.TotalUserJournalCount) {
                journalCount.innerHTML = `${_data.TotalUserJournalCount.toLocaleString()} journals saved`;
            }

            let journalLines = document.querySelector('#stat-journallines');

            if (_data.TotalUserJournalLines) {
                journalLines.innerHTML = `${_data.TotalUserJournalLines.toLocaleString()} lines of journal`;
            }

            let starSystems = document.querySelector('#stat-starsystems');

            if (_data.TotalStarSystemCount) {
                starSystems.innerHTML = `${_data.TotalStarSystemCount.toLocaleString()} star systems`;
            }
        }
    }

    function createToast(title, message, spinnerClass) {
        if (!spinnerClass) spinnerClass = 'warning';

        let toast = $(`<div class="toast">
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
            delay: 5000
        }).toast('show')
    }
}

(function () {
    initializeActivityMonitor();
})();