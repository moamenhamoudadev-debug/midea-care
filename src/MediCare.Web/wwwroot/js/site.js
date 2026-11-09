// MediCare Enterprise Script Engine: UX, Accessibility & Double-Submit Protection

document.addEventListener('DOMContentLoaded', function () {
    // 1. Prevent accidental double-submission and provide instant loading feedback
    document.querySelectorAll('form[method="post"]:not([data-no-double-submit])').forEach(function (form) {
        form.addEventListener('submit', function (e) {
            // If HTML5 form validation fails, let the browser display the error tooltips
            if (form.checkValidity && !form.checkValidity()) {
                return;
            }

            // If jQuery Unobtrusive Validation is attached and invalid, do not disable
            if (typeof $ !== 'undefined' && typeof $.fn.valid === 'function') {
                if ($(form).data('validator') && !$(form).valid()) {
                    return;
                }
            }

            const submitBtn = form.querySelector('button[type="submit"]:not([data-no-spinner])');
            if (submitBtn && !submitBtn.disabled) {
                submitBtn.dataset.originalHtml = submitBtn.innerHTML;

                // Brief timeout ensures form submission data is serialized
                setTimeout(function () {
                    submitBtn.disabled = true;
                    const isAr = document.documentElement.getAttribute('lang') === 'ar';
                    const loadingText = isAr ? 'جاري المعالجة...' : 'Processing...';
                    submitBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-1" role="status" aria-hidden="true"></span> ' + loadingText;
                }, 20);
            }
        });
    });

    // 2. Enhance keyboard focus visibility on interactive elements
    document.querySelectorAll('a, button, input, select, textarea').forEach(function (el) {
        el.addEventListener('focus', function () {
            this.classList.add('focus-visible');
        });
        el.addEventListener('blur', function () {
            this.classList.remove('focus-visible');
        });
    });
});
