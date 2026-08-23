// Reservation administration: AJAX filter/search/sort/pagination.
$(function () {
    var $form = $('#filterForm');
    var $container = $('#resTableContainer');
    var searchTimer = null;

    function loadTable(url) {
        $container.html('<div class="text-center text-muted py-4"><div class="spinner-border spinner-border-sm text-success me-2"></div>Loading reservations…</div>');
        $.get(url, function (html) { $container.html(html); })
            .fail(function () { $container.html('<div class="alert alert-danger">Could not load reservations. Please retry.</div>'); });
    }

    $form.on('submit', function (e) {
        e.preventDefault();
        loadTable($form.attr('action') + '?' + $form.serialize());
    });

    // Selects and the date input refilter immediately; the text search debounces.
    $form.on('change', 'select, input[type="date"]', function () { $form.trigger('submit'); });
    $form.on('input', 'input[name="search"]', function () {
        clearTimeout(searchTimer);
        searchTimer = setTimeout(function () { $form.trigger('submit'); }, 400);
    });

    $(document).on('click', '.page-link-ajax', function (e) {
        e.preventDefault();
        loadTable($(this).attr('href'));
    });
});
