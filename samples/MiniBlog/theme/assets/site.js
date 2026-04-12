(function () {
    const $ = window.jQuery;
    if (!$) {
        return;
    }

    function slugify(value) {
        return (value || "")
            .toLowerCase()
            .trim()
            .replace(/[^a-z0-9]+/g, "-")
            .replace(/^-+|-+$/g, "");
    }

    function ensureToast() {
        const toastElement = document.getElementById("siteToast");
        if (!toastElement || !window.bootstrap) {
            return null;
        }

        const title = document.getElementById("siteToastTitle");
        const body = document.getElementById("siteToastBody");
        const toast = window.bootstrap.Toast.getOrCreateInstance(toastElement);

        return {
            show(message, heading) {
                if (title) {
                    title.textContent = heading || "Northwind Journal";
                }
                if (body) {
                    body.textContent = message;
                }
                toast.show();
            }
        };
    }

    const toaster = ensureToast();

    $(document).on("input", "#Title", function () {
        const slugInput = document.getElementById("Slug");
        if (!slugInput || slugInput.dataset.touched === "true") {
            return;
        }

        slugInput.value = slugify(this.value);
    });

    $(document).on("input", "#Slug", function () {
        this.dataset.touched = "true";
    });

    $(document).on("submit", "form[data-miniblog-editor-form]", function () {
        const form = this;
        setTimeout(function () {
            const summary = form.querySelector("[data-form-summary]");
            if (summary && !summary.classList.contains("d-none") && toaster) {
                toaster.show(summary.textContent || "Please review the editor form.", "Validation");
            }
        }, 25);
    });

    $(document).on("weblogic:form-success", function (_event, payload) {
        if (!payload) {
            return;
        }

        if (toaster) {
            toaster.show(payload.message || "Saved.", "Editor");
        }

        if (payload.redirectUrl) {
            window.setTimeout(function () {
                window.location.href = payload.redirectUrl;
            }, 300);
        }
    });

    const yearTarget = document.querySelector("[data-current-year]");
    if (yearTarget) {
        yearTarget.textContent = new Date().getFullYear().toString();
    }
})();
