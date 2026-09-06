// Booking page: loads the availability slot grid via AJAX, validates the selected
// window client-side, and fills the hidden form fields before submit.
$(function () {
    var $date = $('#bookingDate');
    var $court = $('#bookingCourt');
    var $grid = $('#slotGrid');
    var $error = $('#slotError');
    var $submit = $('#submitBtn');
    var $cartBtn = $('#addToCartBtn');
    var $durationField = $('#durationField');
    var $startField = $('#startTimeField');

    var slots = [];
    var selectedStart = null;

    function duration() { return parseInt($durationField.val(), 10) || 1; }

    function hoursLabel(h) { return h === 1 ? '1 hour' : h + ' hours'; }

    function setSummary(courtName, timeText, rate) {
        $('#sumCourt').text(courtName || '—');
        $('#sumDate').text($date.val() || '—');
        $('#sumTime').text(timeText || '—');
        $('#sumHours').text(selectedStart ? hoursLabel(duration()) : '—');
        $('#sumRate').text(rate ? 'RM ' + rate + '/hr' : '—');
        $('#sumTotal').text(selectedStart && rate ? 'RM ' + (rate * duration()).toFixed(2) : 'RM 0.00');
    }

    function clearSummary() {
        selectedStart = null;
        $startField.val('');
        setSummary(null, null, null);
    }

    // A window is selectable only when every hour from start..start+duration is Open.
    function windowValid(start) {
        var needed = [];
        for (var i = 0; i < duration(); i++) {
            var wanted = addHours(start, i);
            var slot = slots.find(function (s) { return s.startTime === wanted; });
            if (!slot || slot.status !== 'open') return { ok: false, missing: wanted };
            needed.push(slot);
        }
        return { ok: true, end: addHours(start, duration()) };
    }

    function addHours(hhmm, h) {
        var parts = hhmm.split(':');
        var total = parseInt(parts[0], 10) + h;
        return ('0' + total).slice(-2) + ':00';
    }

    function selectStart(start) {
        selectedStart = start;
        $startField.val(start);

        $grid.find('.slot-chip-open').removeClass('selected');
        $grid.find('.slot-chip-open[data-start="' + start + '"]').addClass('selected');

        var check = windowValid(start);
        if (!check.ok) {
            showError('That window is not fully open — slot ' + check.missing + ' is unavailable. Choose a shorter duration or another start time.');
            $submit.prop('disabled', true);
            $cartBtn.prop('disabled', true);
            setSummary(null, null, null);
            return;
        }

        hideError();
        $submit.prop('disabled', false);
        $cartBtn.prop('disabled', false);
        var rate = $('#bookingCourt option:selected').data('rate');
        setSummary($('#bookingCourt option:selected').text(), start + ' – ' + check.end, rate);
    }

    function showError(msg) { $error.text(msg).removeClass('d-none'); }
    function hideError() { $error.addClass('d-none'); }

    function resetForNewSlots() {
        clearSummary();
        hideError();
        $submit.prop('disabled', true);
        $cartBtn.prop('disabled', true);
    }

    function loadSlots() {
        var date = $date.val();
        var courtId = $court.val();
        if (!date || !courtId) {
            $grid.html('<span class="text-muted small">Select a date and court to load availability.</span>');
            resetForNewSlots();
            return;
        }

        $grid.html('<div class="spinner-border spinner-border-sm text-success me-2"></div>Loading slots…');
        resetForNewSlots();

        $.getJSON(slotsUrl, { courtId: courtId, date: date })
            .done(function (res) {
                if (!res.success) {
                    $grid.html('<span class="text-danger small">' + res.message + '</span>');
                    return;
                }
                slots = res.slots;
                $grid.empty();

                // G-M3: at-a-glance remaining-slot indicator above the grid.
                var openCount = slots.filter(function (s) { return s.status === 'open'; }).length;
                var heldCount = slots.filter(function (s) { return s.status === 'held'; }).length;
                var summary = openCount + ' open slot' + (openCount === 1 ? '' : 's');
                if (heldCount > 0) summary += ' · ' + heldCount + ' held';
                $('#slotSummary').text(summary);

                slots.forEach(function (s) {
                    var $b = $('<button type="button" class="btn slot-chip"></button>')
                        .text(s.startTime)
                        .attr('data-start', s.startTime)
                        .attr('data-status', s.status)
                        .attr('title', s.startTime + ' – ' + s.endTime + ' · ' + s.label);
                    if (s.status === 'open') {
                        $b.addClass('slot-chip-open')
                          .on('click', function () { selectStart(s.startTime); });
                    } else {
                        $b.addClass('slot-chip-' + s.status).prop('disabled', true);
                    }
                    $grid.append($b);
                });
                if (!slots.length) {
                    $grid.html('<span class="text-muted small">No slots defined for this date.</span>');
                }
            })
            .fail(function () {
                $grid.html('<span class="text-danger small">Could not load availability. Please try again.</span>');
            });
    }

    $date.on('change', loadSlots);
    $court.on('change', loadSlots);

    $('.duration-btn').on('click', function () {
        $('.duration-btn').removeClass('active');
        $(this).addClass('active');
        $durationField.val($(this).data('hours'));
        if (selectedStart) selectStart(selectedStart);
        else { setSummary(null, null, null); $submit.prop('disabled', true); $cartBtn.prop('disabled', true); }
    });

    // Initial load when arriving from a court page.
    loadSlots();
});
