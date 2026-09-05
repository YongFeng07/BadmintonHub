/* BadmintonHub — admin availability grid (M1 additional feature: AJAX slot management) */
$(function () {
    var token = $('#avToken').val();

    // Quick date buttons jump to another day's grid (same facility).
    $('.date-quick').on('click', function () {
        var offset = parseInt($(this).data('offset'), 10);
        var d = new Date();
        d.setDate(d.getDate() + offset);
        var iso = d.toISOString().slice(0, 10);
        window.location.href = '/AdminAvailability/Index?date=' + iso + '&facilityId=' + $(this).data('facility');
    });

    // Click a slot to cycle Open -> Maintenance -> Blocked -> Open.
    var nextStatus = { open: 'maintenance', maintenance: 'blocked', blocked: 'open', closed: 'open' };

    $('#slotGrid').on('click', '.slot-btn', function () {
        var btn = $(this);
        var next = nextStatus[btn.data('status')] || 'open';

        btn.prop('disabled', true);
        $.post('/AdminAvailability/UpdateSlot', {
            __RequestVerificationToken: token,
            courtId: btn.data('court'),
            date: btn.data('date'),
            startTime: btn.data('start'),
            status: next
        })
            .done(function (res) {
                if (res.success) {
                    btn.removeClass('slot-open slot-maintenance slot-blocked slot-closed')
                        .addClass('slot-' + res.status)
                        .data('status', res.status)
                        .attr('title', btn.data('start') + ' · ' + res.label);
                } else {
                    alert(res.message);
                }
            })
            .fail(function () {
                alert('Update failed. Please try again.');
            })
            .always(function () {
                btn.prop('disabled', false);
            });
    });

    // Bulk status change over a date range.
    $('#rangeForm').on('submit', function (e) {
        e.preventDefault();
        var $result = $('#rangeResult');
        $result.html('<div class="spinner-border spinner-border-sm text-success me-2"></div>Applying…');

        $.post('/AdminAvailability/SetRange', {
            __RequestVerificationToken: $('#avToken2').val(),
            courtId: $('#rangeCourt').val(),
            fromDate: $('#rangeFrom').val(),
            toDate: $('#rangeTo').val(),
            status: $('#rangeStatus').val()
        })
            .done(function (res) {
                if (res.success) {
                    $result.html('<div class="alert alert-success py-2 mb-0">' + res.message + '</div>');
                    // Refresh the grid after a short delay so staff can see the change.
                    setTimeout(function () { window.location.reload(); }, 1200);
                } else {
                    $result.html('<div class="alert alert-danger py-2 mb-0">' + res.message + '</div>');
                }
            })
            .fail(function () {
                $result.html('<div class="alert alert-danger py-2 mb-0">Update failed. Please try again.</div>');
            });
    });
});
