// SportHub shared AJAX list engine (G-M6).
//
// Progressive enhancement: every sort header, page link and filter control has a
// real href/action, so the pages work without JavaScript. With JS enabled the
// list region is refreshed in place, stale requests are aborted, the address bar
// stays shareable (history.replaceState) and batch-select controls stay wired
// after each refresh.
//
// Markup contract:
//   <div data-ajax-list data-url="/Controller/Index">
//     <form data-ajax-filter> …inputs named search/sort/type/… </form>
//     <div data-ajax-target> …rendered list partial (includes _AjaxPager)… </div>
//   </div>
// Sortable headers: <a data-ajax-sort href="?sort=col&dir=asc">…
// Batch selection inside the target: <form data-batch-form> with
//   input[name="itemIds"] checkboxes, [data-batch-select-all], [data-batch-count],
//   and [data-batch-submit] action buttons.
(function ($) {
    'use strict';

    function wireBatch($container) {
        var $form = $container.find('[data-batch-form]');
        if (!$form.length) { return; }

        var $boxes = $container.find('input[name="itemIds"]:checkbox');
        var $selectAll = $container.find('[data-batch-select-all]');
        var $counters = $container.find('[data-batch-count]');

        var refresh = function () {
            var checked = $boxes.filter(':checked').length;
            $counters.text(checked);
            $form.find('[data-batch-submit]').prop('disabled', checked === 0);
            if ($selectAll.length) {
                var all = $boxes.length > 0 && checked === $boxes.length;
                $selectAll.prop('checked', all);
                $selectAll.prop('indeterminate', !all && checked > 0);
            }
        };

        $boxes.off('change.ajaxlist').on('change.ajaxlist', refresh);
        $selectAll.off('change.ajaxlist').on('change.ajaxlist', function () {
            $boxes.prop('checked', this.checked);
            refresh();
        });
        refresh();
    }

    function loadList($list, url) {
        var current = $list.data('xhr');
        if (current) { current.abort(); }

        var $target = $list.find('[data-ajax-target]');
        $target.attr('aria-busy', 'true').addClass('sh-ajax-loading');

        var xhr = $.ajax({
            url: url,
            headers: { 'X-Requested-With': 'XMLHttpRequest' },
            success: function (html) {
                $target.html(html)
                    .attr('aria-busy', 'false')
                    .removeClass('sh-ajax-loading');
                wireBatch($list);
                if (history.replaceState) { history.replaceState(null, '', url); }
            },
            error: function () {
                $target.html(
                    '<div class="sh-empty"><span class="emoji">⚠️</span>' +
                    'Could not refresh the list. <a href="' + $('<div/>').text(url).html() + '">Try again</a></div>'
                ).attr('aria-busy', 'false').removeClass('sh-ajax-loading');
            },
            complete: function () { $list.removeData('xhr'); }
        });
        $list.data('xhr', xhr);
    }

    $(function () {
        $('[data-ajax-list]').each(function () {
            var $list = $(this);
            var baseUrl = $list.data('url');
            var $form = $list.find('[data-ajax-filter]');

            // Filter inputs: debounce keystrokes, fire immediately on select/change.
            if ($form.length) {
                var timer = null;
                $form.on('input change', 'input, select', function (e) {
                    if (e.type === 'input') {
                        clearTimeout(timer);
                        timer = setTimeout(function () { loadList($list, buildUrl()); }, 350);
                    } else {
                        clearTimeout(timer);
                        loadList($list, buildUrl());
                    }
                });
                $form.on('submit', function (e) {
                    e.preventDefault();
                    clearTimeout(timer);
                    loadList($list, buildUrl());
                });
            }

            var buildUrl = function () {
                if (!$form.length) { return baseUrl; }
                var params = $.param($form.serializeArray().filter(function (f) {
                    return f.value !== '';
                }));
                return baseUrl + (params ? '?' + params : '');
            };

            // Sort headers and page links carry their own full query string.
            $list.on('click', '[data-ajax-sort], [data-ajax-page]', function (e) {
                e.preventDefault();
                loadList($list, this.href);
            });

            // Page-size select: keep the current filters, jump back to page 1.
            $list.on('change', '[data-ajax-size]', function () {
                var url = new URL(window.location.href);
                url.searchParams.set('size', this.value);
                url.searchParams.set('page', '1');
                loadList($list, url.search);
            });

            wireBatch($list);
        });
    });
})(jQuery);
