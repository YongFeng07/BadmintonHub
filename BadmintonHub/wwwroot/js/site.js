// Global site scripts.

$(function () {
    // Notification bell / notification centre: mark a single notification as read via AJAX
    // (the form carries the antiforgery token, so the POST passes CSRF validation).
    $(document).on('submit', '.mark-read-form', function (e) {
        e.preventDefault();
        var $form = $(this);
        $.post($form.attr('action'), $form.serialize(), function (res) {
            if (res && res.success) {
                var $item = $form.closest('li');
                $item.fadeOut(200, function () { $item.remove(); });
                var $badge = $('#notificationBadge');
                if ($badge.length) {
                    var count = parseInt($badge.text(), 10);
                    if (!isNaN(count) && count > 1) {
                        $badge.text(count - 1);
                    } else {
                        $badge.remove();
                    }
                }
            }
        });
        return false;
    });

    // Notification centre: "mark all as read".
    $('#markAllReadBtn').on('click', function () {
        var $btn = $(this);
        $.post($btn.data('url'), $btn.closest('form').serialize(), function (res) {
            if (res && res.success) location.reload();
        });
    });
});
