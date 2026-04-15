(function (window, document, $) {
    "use strict";

    if (window.WebLogicClient) {
        return;
    }

    const state = {
        config: {
            navigation: {
                enabled: true,
                autoIntercept: true,
                shellSelector: "[data-weblogic-shell]",
                linkSelector: "a[href]",
                progressColor: "linear-gradient(90deg, #f59e0b 0%, #ef4444 100%)"
            },
            forms: {
                enabled: true,
                autoBind: true,
                ajaxSelector: 'form[data-weblogic-form="ajax"]',
                dynamicSelector: "[data-weblogic-dynamic-url]",
                summarySelector: "[data-form-summary]",
                fieldErrorSelector: "[data-form-error-for]",
                optionsUrl: "/api/weblogic/forms/options",
                searchUrl: "/api/weblogic/forms/search",
                searchDebounceMs: 180
            },
            widgets: {
                actionUrl: "/api/weblogic/widgets/action",
                dataUrl: "/api/weblogic/widgets/data",
                renderUrl: "/api/weblogic/widgets/render",
                areasUrl: "/api/weblogic/widgetareas/render"
            },
            realtime: {
                enabled: true,
                hubUrl: "/weblogic-hubs/events"
            }
        },
        eventHandlers: {},
        navigation: {
            activeRequestId: 0,
            isNavigating: false
        },
        realtime: {
            connection: null,
            status: "idle"
        }
    };

    function normalizeList(values) {
        return (values || []).filter(function (value) {
            return value !== null && value !== undefined && value !== "";
        });
    }

    function emit(name, payload) {
        const handlers = state.eventHandlers[name] || [];
        handlers.forEach(function (handler) {
            try {
                handler(payload);
            } catch (error) {
                console.error("WebLogicClient event handler failed", name, error);
            }
        });
    }

    function on(name, handler) {
        state.eventHandlers[name] = state.eventHandlers[name] || [];
        state.eventHandlers[name].push(handler);
        return function () {
            state.eventHandlers[name] = (state.eventHandlers[name] || []).filter(function (item) {
                return item !== handler;
            });
        };
    }

    function configure(partialConfig) {
        if (!partialConfig) {
            return;
        }

        state.config = $.extend(true, {}, state.config, partialConfig);
    }

    function resolveElement(elementOrSelector) {
        if (!elementOrSelector) {
            return $();
        }

        if (elementOrSelector.jquery) {
            return elementOrSelector;
        }

        if (elementOrSelector.nodeType === 1) {
            return $(elementOrSelector);
        }

        return $(String(elementOrSelector));
    }

    function isModifiedClick(event) {
        return event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0;
    }

    function isInternalUrl(url) {
        try {
            const resolved = new URL(url, window.location.origin);
            return resolved.origin === window.location.origin;
        } catch {
            return false;
        }
    }

    function normalizePath(url) {
        const resolved = new URL(url, window.location.origin);
        return resolved.pathname + resolved.search + resolved.hash;
    }

    function ensureProgressBar() {
        let bar = document.getElementById("weblogic-progress-bar");
        if (bar) {
            return bar;
        }

        bar = document.createElement("div");
        bar.id = "weblogic-progress-bar";
        bar.style.cssText = [
            "position:fixed",
            "top:0",
            "left:0",
            "width:0",
            "height:3px",
            `background:${state.config.navigation.progressColor}`,
            "z-index:12000",
            "opacity:1",
            "transition:width 0.22s ease, opacity 0.28s ease"
        ].join(";");
        document.body.appendChild(bar);
        return bar;
    }

    function startProgress() {
        const bar = ensureProgressBar();
        bar.style.opacity = "1";
        bar.style.width = "0";
        window.setTimeout(function () {
            bar.style.width = "65%";
        }, 8);
    }

    function finishProgress(isFailure) {
        const bar = ensureProgressBar();
        if (isFailure) {
            bar.style.background = "linear-gradient(90deg, #dc2626 0%, #f87171 100%)";
        } else {
            bar.style.background = state.config.navigation.progressColor;
        }

        bar.style.width = "100%";
        window.setTimeout(function () {
            bar.style.opacity = "0";
            bar.style.width = "0";
            if (isFailure) {
                bar.style.background = state.config.navigation.progressColor;
            }
        }, 260);
    }

    function parseDocument(html) {
        return new window.DOMParser().parseFromString(html, "text/html");
    }

    function removeManagedHeadNodes() {
        const managedNodes = document.head.querySelectorAll("[data-weblogic-managed-head]");
        managedNodes.forEach(function (node) {
            node.remove();
        });
    }

    function applyHeadNodes(nodes) {
        removeManagedHeadNodes();

        nodes.forEach(function (node) {
            const clone = node.cloneNode(true);
            clone.setAttribute("data-weblogic-managed-head", "true");
            document.head.appendChild(clone);
        });
    }

    function replaceHeadFromDocument(nextDocument) {
        const nextTitle = nextDocument.querySelector("title");
        if (nextTitle) {
            document.title = nextTitle.textContent || "";
        }

        const incomingNodes = nextDocument.head.querySelectorAll("meta[name], meta[property], link[rel='canonical'], link[rel='alternate']");
        applyHeadNodes(Array.from(incomingNodes));
    }

    function upsertHeadNode(selector, createNode) {
        let node = document.head.querySelector(selector);
        if (!node) {
            node = createNode();
            node.setAttribute("data-weblogic-managed-head", "true");
            document.head.appendChild(node);
        }

        return node;
    }

    function applyMeta(meta) {
        if (!meta) {
            return;
        }

        if (meta.title) {
            document.title = meta.title;
        }

        if (meta.description) {
            upsertHeadNode('meta[name="description"]', function () {
                const tag = document.createElement("meta");
                tag.setAttribute("name", "description");
                return tag;
            }).setAttribute("content", meta.description);
        }

        if (meta.canonical) {
            upsertHeadNode('link[rel="canonical"]', function () {
                const link = document.createElement("link");
                link.setAttribute("rel", "canonical");
                return link;
            }).setAttribute("href", meta.canonical);
        }

        if (meta.robots) {
            upsertHeadNode('meta[name="robots"]', function () {
                const tag = document.createElement("meta");
                tag.setAttribute("name", "robots");
                return tag;
            }).setAttribute("content", meta.robots);
        }

        [["og:title", meta.ogTitle], ["og:description", meta.ogDescription], ["og:image", meta.ogImage], ["og:url", meta.ogUrl], ["og:type", meta.ogType]].forEach(function (pair) {
            if (!pair[1]) {
                return;
            }

            upsertHeadNode(`meta[property="${pair[0]}"]`, function () {
                const tag = document.createElement("meta");
                tag.setAttribute("property", pair[0]);
                return tag;
            }).setAttribute("content", pair[1]);
        });

        [["twitter:title", meta.twitterTitle], ["twitter:description", meta.twitterDescription], ["twitter:image", meta.twitterImage], ["twitter:card", meta.twitterCard]].forEach(function (pair) {
            if (!pair[1]) {
                return;
            }

            upsertHeadNode(`meta[name="${pair[0]}"]`, function () {
                const tag = document.createElement("meta");
                tag.setAttribute("name", pair[0]);
                return tag;
            }).setAttribute("content", pair[1]);
        });

        emit("meta:applied", { meta: meta });
    }

    function executeScripts(container) {
        if (!container) {
            return;
        }

        const scripts = container.querySelectorAll("script");
        scripts.forEach(function (script) {
            const replacement = document.createElement("script");
            Array.from(script.attributes).forEach(function (attribute) {
                replacement.setAttribute(attribute.name, attribute.value);
            });

            if (script.src) {
                replacement.src = script.src;
            } else {
                replacement.textContent = script.textContent;
            }

            script.replaceWith(replacement);
        });
    }

    function swapShell(nextDocument) {
        const selector = state.config.navigation.shellSelector;
        const currentShell = document.querySelector(selector);
        const nextShell = nextDocument.querySelector(selector);

        if (!currentShell || !nextShell) {
            return false;
        }

        const currentLayout = currentShell.getAttribute("data-weblogic-layout") || "";
        const nextLayout = nextShell.getAttribute("data-weblogic-layout") || "";
        if (currentLayout !== nextLayout) {
            return false;
        }

        currentShell.replaceWith(nextShell);
        executeScripts(nextShell);
        return true;
    }

    function fetchPage(url) {
        return window.fetch(url, {
            method: "GET",
            credentials: "same-origin",
            headers: {
                "X-Requested-With": "WebLogicClient",
                "X-WebLogic-Navigate": "true"
            }
        }).then(function (response) {
            if (!response.ok) {
                throw new Error(`Navigation failed with ${response.status}`);
            }

            return response.text();
        });
    }

    function navigate(url, options) {
        const targetUrl = normalizePath(url);
        const settings = $.extend({
            push: true,
            source: "client"
        }, options || {});

        const requestId = ++state.navigation.activeRequestId;
        state.navigation.isNavigating = true;
        startProgress();
        emit("navigate:start", { url: targetUrl, source: settings.source });

        return fetchPage(targetUrl)
            .then(function (html) {
                if (requestId !== state.navigation.activeRequestId) {
                    return;
                }

                const nextDocument = parseDocument(html);
                replaceHeadFromDocument(nextDocument);
                const swapped = swapShell(nextDocument);

                if (!swapped) {
                    window.location.href = targetUrl;
                    return;
                }

                if (settings.push) {
                    window.history.pushState({ url: targetUrl }, "", targetUrl);
                }

                finishProgress(false);
                state.navigation.isNavigating = false;
                emit("navigate:complete", { url: targetUrl, source: settings.source });
            })
            .catch(function (error) {
                finishProgress(true);
                state.navigation.isNavigating = false;
                emit("navigate:error", { url: targetUrl, error: error });
                // Fall back to full page load on navigation failure (404, 403, network error, etc.)
                window.location.href = targetUrl;
            });
    }

    function shouldInterceptLink(link, event) {
        if (!state.config.navigation.enabled || !state.config.navigation.autoIntercept) {
            return false;
        }

        if (!link || isModifiedClick(event)) {
            return false;
        }

        const href = link.getAttribute("href");
        if (!href || href.startsWith("#")) {
            return false;
        }

        if (link.hasAttribute("download") || (link.target && link.target !== "_self")) {
            return false;
        }

        if (link.dataset.weblogicNav === "false") {
            return false;
        }

        return isInternalUrl(href);
    }

    function bindNavigation() {
        document.addEventListener("click", function (event) {
            const link = event.target.closest(state.config.navigation.linkSelector);
            if (!shouldInterceptLink(link, event)) {
                return;
            }

            event.preventDefault();
            navigate(link.href, {
                push: true,
                source: "link"
            });
        });

        window.history.replaceState({ url: normalizePath(window.location.href) }, "", window.location.href);

        window.addEventListener("popstate", function (event) {
            var url = (event.state && event.state.url) ? event.state.url : normalizePath(window.location.href);
            navigate(url, {
                push: false,
                source: "history"
            });
        });
    }

    function initialize() {
        bindNavigation();
        bindForms();
        bindWidgets();

        if (state.config.realtime.enabled) {
            connectRealtime().catch(function () {});
        }

        on("navigate:complete", function () {
            bindWidgets();
        });

        emit("ready", {});
    }

    function serializeForm(form) {
        return new window.FormData(form);
    }

    function getFormSchema(form) {
        const schemaId = form.getAttribute("data-weblogic-form-schema");
        if (!schemaId) {
            return null;
        }

        const script = document.getElementById(schemaId);
        if (!script) {
            return null;
        }

        try {
            return JSON.parse(script.textContent || "{}");
        } catch (error) {
            console.error("WebLogicClient form schema parse failed", schemaId, error);
            return null;
        }
    }

    function getFieldElements(form, fieldName) {
        const elements = Array.from(form.querySelectorAll(`[name="${CSS.escape(fieldName)}"]`));
        const display = form.querySelector(`[data-weblogic-autocomplete-display-for="${CSS.escape(fieldName)}"]`);
        if (display) {
            elements.push(display);
        }
        return elements;
    }

    function getScalarFieldValue(form, field) {
        const elements = getFieldElements(form, field.name);
        if (!elements.length) {
            return "";
        }

        const first = elements[0];
        if (first.type === "checkbox") {
            return first.checked ? (first.value || "true") : "";
        }

        if (first.type === "radio") {
            const checked = elements.find(function (element) { return element.checked; });
            return checked ? (checked.value || "") : "";
        }

        return first.value || "";
    }

    function applySelectOptions(select, options, schemaField) {
        const currentValue = select.value || "";
        const prompt = schemaField && schemaField.selectPrompt ? schemaField.selectPrompt : "Pick an option";
        const fragments = [`<option value="">${prompt}</option>`];
        (options || []).forEach(function (option) {
            fragments.push(`<option value="${option.value}">${option.label}</option>`);
        });
        select.innerHTML = fragments.join("");

        if (currentValue && (options || []).some(function (option) { return option.value === currentValue; })) {
            select.value = currentValue;
        }
    }

    function loadFieldOptions(form, schemaField) {
        if (!schemaField || !schemaField.optionsProvider) {
            return Promise.resolve([]);
        }

        const params = new URLSearchParams();
        params.set("formId", form.getAttribute("data-weblogic-form-id") || (getFormSchema(form) || {}).id || "");
        params.set("field", schemaField.name);
        const dependsOn = schemaField.dependsOn;
        if (dependsOn) {
            params.set(dependsOn, getScalarFieldValue(form, { name: dependsOn }));
        }

        return window.fetch(`${state.config.forms.optionsUrl}?${params.toString()}`, {
            method: "GET",
            credentials: "same-origin",
            headers: {
                "X-Requested-With": "WebLogicClient"
            }
        }).then(function (response) {
            return response.json();
        }).then(function (payload) {
            return payload.options || [];
        });
    }

    function loadFieldSearch(form, schemaField, term) {
        if (!schemaField || !(schemaField.searchProvider || schemaField.optionsProvider)) {
            return Promise.resolve([]);
        }

        const params = new URLSearchParams();
        params.set("formId", form.getAttribute("data-weblogic-form-id") || (getFormSchema(form) || {}).id || "");
        params.set("field", schemaField.name);
        params.set("term", term || "");
        const dependsOn = schemaField.dependsOn;
        if (dependsOn) {
            params.set(dependsOn, getScalarFieldValue(form, { name: dependsOn }));
        }

        return window.fetch(`${state.config.forms.searchUrl}?${params.toString()}`, {
            method: "GET",
            credentials: "same-origin",
            headers: {
                "X-Requested-With": "WebLogicClient"
            }
        }).then(function (response) {
            return response.json();
        }).then(function (payload) {
            return payload.options || [];
        });
    }

    function bindProviderOptions(form) {
        const schema = getFormSchema(form);
        if (!schema || !schema.fields || !schema.fields.length) {
            return;
        }

        const providerFields = schema.fields.filter(function (field) { return !!field.optionsProvider; });
        if (!providerFields.length) {
            return;
        }

        providerFields.forEach(function (field) {
            const select = form.querySelector(`select[name="${CSS.escape(field.name)}"]`);
            if (!select) {
                return;
            }

            const refresh = function () {
                loadFieldOptions(form, field)
                    .then(function (options) {
                        applySelectOptions(select, options, field);
                        emit("forms:options-loaded", { form: form, field: field.name, options: options });
                    })
                    .catch(function (error) {
                        emit("forms:options-error", { form: form, field: field.name, error: error });
                    });
            };

            refresh();
            if (field.dependsOn) {
                const dependency = form.querySelector(`[name="${CSS.escape(field.dependsOn)}"]`);
                if (dependency) {
                    dependency.addEventListener("change", refresh);
                }
            }
        });
    }

    function bindAutocompleteFields(form) {
        const schema = getFormSchema(form);
        if (!schema || !schema.fields || !schema.fields.length) {
            return;
        }

        const searchFields = schema.fields.filter(function (field) {
            return field.inputType === "autocomplete" && !!(field.searchProvider || field.optionsProvider);
        });
        if (!searchFields.length) {
            return;
        }

        searchFields.forEach(function (field) {
            const wrapper = form.querySelector(`[data-weblogic-autocomplete-field="${CSS.escape(field.name)}"]`);
            if (!wrapper) {
                return;
            }

            const hiddenInput = form.querySelector(`input[type="hidden"][name="${CSS.escape(field.name)}"]`);
            const textInput = wrapper.querySelector(`[data-weblogic-autocomplete-display-for="${CSS.escape(field.name)}"]`);
            const resultsPanel = wrapper.querySelector("[data-weblogic-autocomplete-results]");
            if (!hiddenInput || !textInput || !resultsPanel) {
                return;
            }

            const minLength = Number(field.minSearchLength || textInput.getAttribute("data-weblogic-search-min-length") || 2);
            let searchTimeout = 0;

            function clearResults() {
                resultsPanel.innerHTML = "";
                resultsPanel.classList.add("d-none");
            }

            function setSelection(option) {
                hiddenInput.value = option && option.value ? option.value : "";
                textInput.value = option && option.label ? option.label : "";
                clearResults();
                emit("forms:autocomplete-selected", {
                    form: form,
                    field: field.name,
                    option: option || null
                });
            }

            function renderResults(options) {
                if (!options || !options.length) {
                    resultsPanel.innerHTML = '<button class="list-group-item list-group-item-action disabled" type="button">No matches found.</button>';
                    resultsPanel.classList.remove("d-none");
                    return;
                }

                resultsPanel.innerHTML = options.map(function (option) {
                    return `<button class="list-group-item list-group-item-action" type="button" data-value="${option.value}" data-label="${option.label}">${option.label}</button>`;
                }).join("");
                resultsPanel.classList.remove("d-none");
            }

            function search(term) {
                if ((term || "").trim().length < minLength) {
                    clearResults();
                    return;
                }

                loadFieldSearch(form, field, term)
                    .then(function (options) {
                        renderResults(options);
                        emit("forms:search-loaded", { form: form, field: field.name, options: options, term: term });
                    })
                    .catch(function (error) {
                        clearResults();
                        emit("forms:search-error", { form: form, field: field.name, error: error, term: term });
                    });
            }

            textInput.addEventListener("input", function () {
                hiddenInput.value = "";
                window.clearTimeout(searchTimeout);
                searchTimeout = window.setTimeout(function () {
                    search(textInput.value || "");
                }, state.config.forms.searchDebounceMs);
            });

            textInput.addEventListener("focus", function () {
                if ((textInput.value || "").trim().length >= minLength) {
                    search(textInput.value || "");
                }
            });

            textInput.addEventListener("blur", function () {
                window.setTimeout(clearResults, 140);
            });

            resultsPanel.addEventListener("click", function (event) {
                const button = event.target.closest("[data-value]");
                if (!button) {
                    return;
                }

                setSelection({
                    value: button.getAttribute("data-value") || "",
                    label: button.getAttribute("data-label") || button.textContent || ""
                });
            });

            if (field.dependsOn) {
                const dependency = form.querySelector(`[name="${CSS.escape(field.dependsOn)}"]`);
                if (dependency) {
                    dependency.addEventListener("change", function () {
                        setSelection(null);
                    });
                }
            }
        });
    }

    function getFileValue(form, field) {
        const input = form.querySelector(`input[type="file"][name="${CSS.escape(field.name)}"]`);
        return input && input.files && input.files.length ? input.files[0] : null;
    }

    function clearFormErrors(form) {
        form.querySelectorAll(state.config.forms.fieldErrorSelector).forEach(function (node) {
            node.textContent = "";
            node.classList.add("d-none");
        });

        form.querySelectorAll(".is-invalid").forEach(function (node) {
            node.classList.remove("is-invalid");
        });

        const summary = form.querySelector(state.config.forms.summarySelector);
        if (summary) {
            summary.innerHTML = "";
            summary.classList.add("d-none");
        }
    }

    function setFormErrors(form, errors) {
        clearFormErrors(form);
        if (!errors || !errors.length) {
            return;
        }

        const summary = form.querySelector(state.config.forms.summarySelector);
        if (summary) {
            summary.innerHTML = `<ul class="mb-0">${errors.map(function (error) {
                return `<li>${error.message}</li>`;
            }).join("")}</ul>`;
            summary.classList.remove("d-none");
        }

        errors.forEach(function (error) {
            const fieldElements = getFieldElements(form, error.fieldName);
            fieldElements.forEach(function (element) {
                element.classList.add("is-invalid");
            });

            const target = form.querySelector(`[data-form-error-for="${CSS.escape(error.fieldName)}"]`);
            if (target) {
                target.textContent = error.message;
                target.classList.remove("d-none");
            }
        });
    }

    function getImageDimensions(file) {
        return new Promise(function (resolve, reject) {
            const objectUrl = window.URL.createObjectURL(file);
            const image = new window.Image();
            image.onload = function () {
                const dimensions = {
                    width: image.naturalWidth || image.width,
                    height: image.naturalHeight || image.height
                };
                window.URL.revokeObjectURL(objectUrl);
                resolve(dimensions);
            };
            image.onerror = function () {
                window.URL.revokeObjectURL(objectUrl);
                reject(new Error("Image load failed."));
            };
            image.src = objectUrl;
        });
    }

    async function validateFileField(form, field, errors) {
        const file = getFileValue(form, field);
        const fileRules = field.file || null;

        if (!file) {
            if (field.required) {
                errors.push({ fieldName: field.name, code: "required", message: `${field.label} is required.` });
            }
            return;
        }

        if (fileRules && fileRules.maxFileSizeBytes && file.size > fileRules.maxFileSizeBytes) {
            errors.push({ fieldName: field.name, code: "max_file_size", message: `${field.label} exceeds the allowed file size.` });
        }

        const lowerExtension = file.name && file.name.indexOf(".") >= 0
            ? `.${file.name.split(".").pop().toLowerCase()}`
            : "";

        if (fileRules && fileRules.allowedExtensions && fileRules.allowedExtensions.length && lowerExtension && fileRules.allowedExtensions.map(function (value) { return String(value).toLowerCase(); }).indexOf(lowerExtension) < 0) {
            errors.push({ fieldName: field.name, code: "extension", message: `${field.label} has an unsupported file extension.` });
        }

        if (fileRules && fileRules.allowedContentTypes && fileRules.allowedContentTypes.length && file.type && fileRules.allowedContentTypes.map(function (value) { return String(value).toLowerCase(); }).indexOf(String(file.type).toLowerCase()) < 0) {
            errors.push({ fieldName: field.name, code: "content_type", message: `${field.label} has an unsupported content type.` });
        }

        const requiresImageChecks = fileRules && (
            fileRules.requireImage ||
            fileRules.maxImageWidth ||
            fileRules.maxImageHeight ||
            fileRules.minImageWidth ||
            fileRules.minImageHeight);

        if (!requiresImageChecks || !file) {
            return;
        }

        try {
            const dimensions = await getImageDimensions(file);
            if (fileRules.maxImageWidth && dimensions.width > fileRules.maxImageWidth) {
                errors.push({ fieldName: field.name, code: "max_image_width", message: `${field.label} is wider than allowed.` });
            }
            if (fileRules.maxImageHeight && dimensions.height > fileRules.maxImageHeight) {
                errors.push({ fieldName: field.name, code: "max_image_height", message: `${field.label} is taller than allowed.` });
            }
            if (fileRules.minImageWidth && dimensions.width < fileRules.minImageWidth) {
                errors.push({ fieldName: field.name, code: "min_image_width", message: `${field.label} is narrower than required.` });
            }
            if (fileRules.minImageHeight && dimensions.height < fileRules.minImageHeight) {
                errors.push({ fieldName: field.name, code: "min_image_height", message: `${field.label} is shorter than required.` });
            }
        } catch {
            errors.push({ fieldName: field.name, code: "image", message: `${field.label} must be a valid image file.` });
        }
    }

    async function validateForm(form) {
        const schema = getFormSchema(form);
        if (!schema || !schema.fields || !schema.fields.length) {
            return { isValid: true, errors: [], schema: null };
        }

        const errors = [];
        for (const field of schema.fields) {
            if (field.inputType === "file") {
                await validateFileField(form, field, errors);
                continue;
            }

            const rawValue = String(getScalarFieldValue(form, field) || "");
            if (field.required && !rawValue.trim()) {
                errors.push({ fieldName: field.name, code: "required", message: `${field.label} is required.` });
                continue;
            }

            if (!rawValue.trim()) {
                continue;
            }

            if (field.minLength && rawValue.length < field.minLength) {
                errors.push({ fieldName: field.name, code: "min_length", message: `${field.label} must be at least ${field.minLength} characters long.` });
            }

            if (field.maxLength && rawValue.length > field.maxLength) {
                errors.push({ fieldName: field.name, code: "max_length", message: `${field.label} must be no more than ${field.maxLength} characters long.` });
            }

            if (field.pattern) {
                const regex = new RegExp(field.pattern);
                if (!regex.test(rawValue)) {
                    errors.push({ fieldName: field.name, code: "pattern", message: `${field.label} is not in the expected format.` });
                }
            }

            if (field.allowedValues && field.allowedValues.length && field.allowedValues.indexOf(rawValue) < 0) {
                errors.push({ fieldName: field.name, code: "allowed_values", message: `${field.label} must be one of: ${field.allowedValues.join(", ")}.` });
            }

            if ((field.inputType === "number") && rawValue) {
                const numberValue = Number(rawValue);
                if (!Number.isFinite(numberValue)) {
                    errors.push({ fieldName: field.name, code: "number", message: `${field.label} must be a valid number.` });
                } else {
                    if (field.minValue !== null && field.minValue !== undefined && numberValue < Number(field.minValue)) {
                        errors.push({ fieldName: field.name, code: "min_value", message: `${field.label} must be at least ${field.minValue}.` });
                    }
                    if (field.maxValue !== null && field.maxValue !== undefined && numberValue > Number(field.maxValue)) {
                        errors.push({ fieldName: field.name, code: "max_value", message: `${field.label} must be no more than ${field.maxValue}.` });
                    }
                }
            }
        }

        setFormErrors(form, errors);
        emit("forms:validated", { form: form, schema: schema, errors: errors, isValid: errors.length === 0 });
        return { isValid: errors.length === 0, errors: errors, schema: schema };
    }

    function handleAjaxForm(form, options) {
        const settings = $.extend({
            method: (form.getAttribute("method") || "POST").toUpperCase(),
            url: form.getAttribute("action") || window.location.href
        }, options || {});

        return validateForm(form).then(function (validation) {
            if (!validation.isValid) {
                emit("forms:submit-blocked", { form: form, url: settings.url, errors: validation.errors });
                return Promise.reject(new Error("Client validation failed."));
            }

            const formData = serializeForm(form);
            emit("forms:submit-start", { form: form, url: settings.url });

            return window.fetch(settings.url, {
                method: settings.method,
                body: formData,
                credentials: "same-origin",
                headers: csrfHeaders({
                    "X-Requested-With": "WebLogicClient"
                })
            }).then(function (response) {
                const contentType = response.headers.get("content-type") || "";
                if (contentType.indexOf("application/json") >= 0) {
                    return response.json().then(function (payload) {
                        if (payload && payload.errors && Array.isArray(payload.errors)) {
                            setFormErrors(form, payload.errors.map(function (error) {
                                return {
                                    fieldName: error.fieldName || error.FieldName,
                                    code: error.code || error.Code,
                                    message: error.message || error.Message
                                };
                            }));
                        } else {
                            clearFormErrors(form);
                        }

                        emit("forms:submit-complete", { form: form, url: settings.url, response: payload });
                        return payload;
                    });
                }

                clearFormErrors(form);
                return response.text().then(function (html) {
                    emit("forms:submit-complete", { form: form, url: settings.url, response: html });
                    return html;
                });
            }).catch(function (error) {
                emit("forms:submit-error", { form: form, url: settings.url, error: error });
                throw error;
            });
        });
    }

    function handleDynamicInput(element) {
        const targetUrl = element.getAttribute("data-weblogic-dynamic-url");
        if (!targetUrl) {
            return;
        }

        const form = element.form || element.closest("form");
        const formData = new window.FormData(form || undefined);
        emit("forms:dynamic-start", { element: element, url: targetUrl });

        window.fetch(targetUrl, {
            method: "POST",
            body: formData,
            credentials: "same-origin",
            headers: {
                "X-Requested-With": "WebLogicClient"
            }
        }).then(function (response) {
            return response.json();
        }).then(function (payload) {
            emit("forms:dynamic-complete", { element: element, url: targetUrl, response: payload });
        }).catch(function (error) {
            emit("forms:dynamic-error", { element: element, url: targetUrl, error: error });
        });
    }

    function bindForms() {
        if (!state.config.forms.enabled || !state.config.forms.autoBind) {
            return;
        }

        function initializeForm(form) {
            if (!form || form.dataset.weblogicFormsBound === "true") {
                return;
            }

            bindProviderOptions(form);
            bindAutocompleteFields(form);
            form.dataset.weblogicFormsBound = "true";
        }

        document.querySelectorAll(state.config.forms.ajaxSelector).forEach(initializeForm);

        document.addEventListener("submit", function (event) {
            const form = event.target.closest(state.config.forms.ajaxSelector);
            if (!form) {
                return;
            }

            initializeForm(form);
            event.preventDefault();
            handleAjaxForm(form);
        });

        let dynamicTimeout = 0;
        document.addEventListener("change", function (event) {
            const element = event.target.closest(state.config.forms.dynamicSelector);
            if (!element) {
                return;
            }

            window.clearTimeout(dynamicTimeout);
            dynamicTimeout = window.setTimeout(function () {
                handleDynamicInput(element);
            }, 80);
        });

        on("navigate:complete", function () {
            document.querySelectorAll(state.config.forms.ajaxSelector).forEach(initializeForm);
        });
    }

    function showToast(title, detail, options) {
        const settings = $.extend({
            toastSelector: "#siteToast",
            titleSelector: "#siteToastTitle",
            bodySelector: "#siteToastBody",
            delay: 3000
        }, options || {});

        const toastElement = document.querySelector(settings.toastSelector);
        if (!toastElement || typeof window.bootstrap === "undefined") {
            emit("ui:toast-missed", { title: title, detail: detail });
            return;
        }

        const titleElement = document.querySelector(settings.titleSelector);
        const bodyElement = document.querySelector(settings.bodySelector);
        if (titleElement) {
            titleElement.textContent = title || "WebLogic";
        }
        if (bodyElement) {
            bodyElement.textContent = detail || "";
        }

        const toast = window.bootstrap.Toast.getOrCreateInstance(toastElement, {
            delay: settings.delay
        });
        toast.show();
        emit("ui:toast", { title: title, detail: detail });
    }

    function emitWidgetChannel(channel, payload) {
        if (!channel) {
            return;
        }

        const message = {
            channel: String(channel),
            payload: payload || {}
        };

        $(document).trigger("weblogic:widget-channel", [message]);
        emit("widgets:channel", message);
    }

    function renderWidgetHtml(request) {
        return $.ajax({
            url: state.config.widgets.renderUrl,
            method: "GET",
            data: request || {},
            dataType: "html"
        });
    }

    function refreshWidgetInstance(elementOrSelector) {
        const $container = resolveElement(elementOrSelector).first();
        const widgetName = $container.data("widget-name");
        const instanceId = $container.data("widget-instance");
        if (!$container.length || !widgetName) {
            return $.Deferred().resolve().promise();
        }

        return renderWidgetHtml({
            name: widgetName,
            instanceId: instanceId
        }).done(function (html) {
            const $replacement = $(html);
            $container.replaceWith($replacement);
            emit("widgets:refreshed", {
                instanceId: instanceId,
                widgetName: widgetName,
                element: $replacement
            });
        });
    }

    function refreshWidgetInstanceById(instanceId) {
        if (!instanceId) {
            return $.Deferred().resolve().promise();
        }

        const $widgets = $(`[data-widget-instance="${CSS.escape(instanceId)}"]`);
        if (!$widgets.length) {
            return $.Deferred().resolve().promise();
        }

        const jobs = [];
        $widgets.each(function () {
            jobs.push(refreshWidgetInstance($(this)));
        });

        return $.when.apply($, jobs);
    }

    function loadWidgetData(elementOrOptions) {
        let widgetName = "";
        let instanceId = "";

        if (elementOrOptions && (elementOrOptions.jquery || elementOrOptions.nodeType === 1 || typeof elementOrOptions === "string")) {
            const $container = resolveElement(elementOrOptions).first();
            widgetName = $container.data("widget-name");
            instanceId = $container.data("widget-instance");
        } else {
            widgetName = elementOrOptions && elementOrOptions.name;
            instanceId = elementOrOptions && elementOrOptions.instanceId;
        }

        if (!widgetName) {
            return $.Deferred().reject(new Error("Widget name is required.")).promise();
        }

        return $.getJSON(state.config.widgets.dataUrl, {
            name: widgetName,
            instanceId: instanceId
        }).done(function (response) {
            emit("widgets:data", {
                widgetName: widgetName,
                instanceId: instanceId,
                response: response
            });
        });
    }

    function renderWidgetAreaHtml(name, targetPath) {
        return $.ajax({
            url: state.config.widgets.areasUrl,
            method: "GET",
            data: {
                name: name,
                targetPath: targetPath || window.location.pathname
            },
            dataType: "html"
        });
    }

    function refreshWidgetAreaCards(areaName) {
        const $cards = $(`[data-widget-area-card="${CSS.escape(areaName)}"]`);
        if (!$cards.length) {
            return $.Deferred().resolve().promise();
        }

        return renderWidgetAreaHtml(areaName, window.location.pathname)
            .done(function (html) {
                $cards.html(html || `<div class="glass-card p-4 rounded-4 text-secondary">No accessible widgets in this area.</div>`);
                emit("widgets:area-refreshed", { areaName: areaName, target: "cards" });
            })
            .fail(function () {
                $cards.html(`<div class="glass-card p-4 rounded-4 text-secondary">The area preview failed to load.</div>`);
            });
    }

    function refreshWidgetAreaRegions(areaName) {
        const $regions = $(`[data-widget-area-region="${CSS.escape(areaName)}"]`);
        if (!$regions.length) {
            return $.Deferred().resolve().promise();
        }

        const jobs = [];
        $regions.each(function () {
            const $region = $(this);
            const targetPath = $region.data("widget-area-target") || window.location.pathname;
            jobs.push(renderWidgetAreaHtml(areaName, targetPath)
                .done(function (html) {
                    $region.html(html || "");
                    emit("widgets:area-refreshed", { areaName: areaName, target: "region", element: $region });
                }));
        });

        return $.when.apply($, jobs);
    }

    function refreshWidgetArea(areaName) {
        return $.when(
            refreshWidgetAreaCards(areaName),
            refreshWidgetAreaRegions(areaName)
        );
    }

    function refreshWidgetAreas(areaNames) {
        const names = normalizeList(areaNames);
        if (!names.length) {
            return $.Deferred().resolve().promise();
        }

        const jobs = names.map(function (name) {
            return refreshWidgetArea(name);
        });

        return $.when.apply($, jobs);
    }

    function runWidgetAction(request) {
        return $.post(state.config.widgets.actionUrl, request || {});
    }

    function handleWidgetActionResponse(response, options) {
        const settings = $.extend({
            widget: null,
            onMessage: null,
            onChannel: null,
            onRefreshDashboard: null,
            onRefreshWidgetCatalog: null,
            onRefreshWidgetAreas: null
        }, options || {});

        const refresh = response.refresh || {};
        const messages = response.messages || [];
        const events = response.events || [];
        const widgetInstances = normalizeList(refresh.WidgetInstances || refresh.widgetInstances);
        const widgetAreas = normalizeList(refresh.WidgetAreas || refresh.widgetAreas);
        const jobs = [];

        messages.forEach(function (message) {
            const normalized = {
                title: message.Title || message.title || "Widget message",
                detail: message.Detail || message.detail || ""
            };
            if (typeof settings.onMessage === "function") {
                settings.onMessage(normalized, response);
            }
            emit("widgets:message", normalized);
        });

        events.forEach(function (evt) {
            const channel = evt.Channel || evt.channel;
            const payload = evt.Payload || evt.payload || {};
            emitWidgetChannel(channel, payload);
            if (typeof settings.onChannel === "function") {
                settings.onChannel({ channel: channel, payload: payload }, response);
            }
        });

        if (settings.widget && !widgetInstances.length) {
            jobs.push(refreshWidgetInstance(settings.widget));
        }

        widgetInstances.forEach(function (instanceId) {
            jobs.push(refreshWidgetInstanceById(instanceId));
        });

        widgetAreas.forEach(function (areaName) {
            jobs.push(refreshWidgetArea(areaName));
        });

        if ((refresh.RefreshWidgetAreas || refresh.refreshWidgetAreas) && typeof settings.onRefreshWidgetAreas === "function") {
            jobs.push(settings.onRefreshWidgetAreas(response));
        }

        if ((refresh.RefreshWidgetCatalog || refresh.refreshWidgetCatalog) && typeof settings.onRefreshWidgetCatalog === "function") {
            jobs.push(settings.onRefreshWidgetCatalog(response));
        }

        if ((refresh.RefreshDashboard || refresh.refreshDashboard) && typeof settings.onRefreshDashboard === "function") {
            jobs.push(settings.onRefreshDashboard(response));
        }

        emit("widgets:action-complete", { response: response, widget: settings.widget });
        return jobs.length ? $.when.apply($, jobs) : $.Deferred().resolve().promise();
    }

    function bindWidgets() {
        // Auto-bind widget action buttons: <button data-widget-action="actionName" data-widget-name="myWidget">
        $(document).off("click.weblogic-widget-action").on("click.weblogic-widget-action", "[data-widget-action]", function (e) {
            e.preventDefault();
            var $btn = $(this);
            var $container = $btn.closest("[data-widget-name]");
            var actionName = $btn.data("widget-action");
            var widgetName = $btn.data("widget-name") || $container.data("widget-name");
            var instanceId = $btn.data("widget-instance") || $container.data("widget-instance") || "";

            if (!actionName || !widgetName) {
                return;
            }

            var payload = {};
            $btn.find("input, select, textarea").each(function () {
                payload[this.name] = $(this).val();
            });
            $.extend(payload, $btn.data());

            $btn.prop("disabled", true);
            emit("widgets:action-start", { action: actionName, widget: widgetName, instance: instanceId });

            runWidgetAction({
                name: widgetName,
                instanceId: instanceId,
                action: actionName,
                payload: JSON.stringify(payload)
            }).done(function (response) {
                handleWidgetActionResponse(response, {
                    widget: $container.length ? $container : null
                });
            }).fail(function (error) {
                emit("widgets:action-error", { action: actionName, widget: widgetName, error: error });
            }).always(function () {
                $btn.prop("disabled", false);
            });
        });

        // Auto-bind widget refresh buttons: <button data-widget-refresh="instanceId">
        $(document).off("click.weblogic-widget-refresh").on("click.weblogic-widget-refresh", "[data-widget-refresh]", function (e) {
            e.preventDefault();
            var instanceId = $(this).data("widget-refresh");
            if (instanceId === true || instanceId === "self") {
                refreshWidgetInstance($(this).closest("[data-widget-name]"));
            } else {
                refreshWidgetInstanceById(String(instanceId));
            }
        });

        // Auto-bind widget area refresh: <button data-widget-area-refresh="areaName">
        $(document).off("click.weblogic-widget-area-refresh").on("click.weblogic-widget-area-refresh", "[data-widget-area-refresh]", function (e) {
            e.preventDefault();
            refreshWidgetArea($(this).data("widget-area-refresh"));
        });

        // Auto-load widget data on init: <div data-widget-name="myWidget" data-widget-autoload="true">
        $("[data-widget-autoload]").each(function () {
            var $el = $(this);
            if ($el.data("widget-autoloaded")) {
                return;
            }
            $el.data("widget-autoloaded", true);

            loadWidgetData($el).done(function (data) {
                emit("widgets:autoloaded", { element: $el, data: data });
            });
        });
    }

    function setRealtimeStatus(value) {
        state.realtime.status = value;
        document.querySelectorAll("[data-signalr-status]").forEach(function (node) {
            node.textContent = value;
        });
        emit("realtime:status", { status: value });
    }

    function connectRealtime(options) {
        const settings = $.extend({
            hubUrl: state.config.realtime.hubUrl
        }, options || {});

        if (!state.config.realtime.enabled || typeof window.signalR === "undefined" || !window.signalR.HubConnectionBuilder) {
            setRealtimeStatus("polling fallback");
            return $.Deferred().resolve(null).promise();
        }

        if (state.realtime.connection) {
            return $.Deferred().resolve(state.realtime.connection).promise();
        }

        const connection = new window.signalR.HubConnectionBuilder()
            .withUrl(settings.hubUrl)
            .withAutomaticReconnect()
            .build();

        connection.on("weblogic:event", function (event) {
            const properties = event.properties || event.Properties || {};
            const widgetChannel = properties.widgetChannel || properties.WidgetChannel;
            if (widgetChannel) {
                emitWidgetChannel(widgetChannel, event.payload || event.Payload || {});
            }

            emit("realtime:event", { event: event });
        });

        connection.onreconnecting(function () {
            setRealtimeStatus("reconnecting");
        });

        connection.onreconnected(function () {
            setRealtimeStatus("connected");
            emit("realtime:reconnected", {});
        });

        connection.onclose(function () {
            state.realtime.connection = null;
            setRealtimeStatus("disconnected");
            emit("realtime:closed", {});
        });

        const deferred = $.Deferred();
        connection.start()
            .then(function () {
                state.realtime.connection = connection;
                setRealtimeStatus("connected");
                emit("realtime:connected", { connection: connection });
                deferred.resolve(connection);
            })
            .catch(function (error) {
                setRealtimeStatus("polling fallback");
                emit("realtime:error", { error: error });
                deferred.reject(error);
            });

        return deferred.promise();
    }

    // ---- Server Command Processor ----
    // JSON responses with a "commands" array get auto-processed.
    // Supported: toast, redirect, reload, navigate, eval
    function processCommands(commands) {
        if (!commands || !Array.isArray(commands)) return;

        var hasToast = false;
        var deferredAction = null;
        var overlayDuration = 0;

        // First pass: execute immediate commands (toast, replace, remove, class changes)
        commands.forEach(function (cmd) {
            if (!cmd || !cmd.type) return;
            switch (cmd.type) {
                case "toast":
                    if (window.FH && window.FH.toast) {
                        window.FH.toast(cmd.message || "", cmd.variant || "success");
                    } else {
                        showToast(cmd.message || "", cmd.detail || "", {});
                    }
                    hasToast = true;
                    break;
                case "overlay":
                    if (window.FH && window.FH.overlay) {
                        var dur = cmd.duration || 2000;
                        window.FH.overlay({
                            variant: cmd.variant || "success",
                            title: cmd.title || "",
                            message: cmd.message || "",
                            duration: dur
                        });
                        overlayDuration = dur;
                    }
                    hasToast = true;
                    break;
                case "redirect":
                case "navigate":
                case "reload":
                    // Defer navigation — show toast first
                    if (!deferredAction) deferredAction = cmd;
                    break;
                case "replace":
                    if (cmd.selector && cmd.html != null) {
                        var target = document.querySelector(cmd.selector);
                        if (target) target.innerHTML = cmd.html;
                    }
                    break;
                case "remove":
                    if (cmd.selector) {
                        var el = document.querySelector(cmd.selector);
                        if (el) el.remove();
                    }
                    break;
                case "addClass":
                    if (cmd.selector && cmd.className) {
                        var el2 = document.querySelector(cmd.selector);
                        if (el2) el2.classList.add(cmd.className);
                    }
                    break;
                case "removeClass":
                    if (cmd.selector && cmd.className) {
                        var el3 = document.querySelector(cmd.selector);
                        if (el3) el3.classList.remove(cmd.className);
                    }
                    break;
            }
        });

        // Second pass: execute deferred navigation after toast is visible
        if (deferredAction) {
            var delay = overlayDuration > 0 ? overlayDuration : (hasToast ? 1500 : 0);
            setTimeout(function () {
                switch (deferredAction.type) {
                    case "redirect":
                        if (deferredAction.url) window.location.href = deferredAction.url;
                        break;
                    case "navigate":
                        if (deferredAction.url) navigate(deferredAction.url);
                        break;
                    case "reload":
                        window.location.reload();
                        break;
                }
            }, delay);
        }
    }

    // Auto-process commands from form submissions
    on("forms:submit-complete", function (data) {
        if (data && data.response && typeof data.response === "object" && data.response.commands) {
            processCommands(data.response.commands);
        }
    });

    function getCsrfToken() {
        var meta = document.querySelector('meta[name="csrf-token"]');
        return meta ? (meta.getAttribute("content") || "") : "";
    }

    function csrfHeaders(extra) {
        var headers = extra ? Object.assign({}, extra) : {};
        var token = getCsrfToken();
        if (token) headers["X-CSRF-Token"] = token;
        return headers;
    }

    window.WebLogicClient = {
        configure: configure,
        on: on,
        emit: emit,
        state: state,
        navigation: {
            navigate: navigate,
            bind: bindNavigation
        },
        meta: {
            applyDocument: replaceHeadFromDocument,
            applyMeta: applyMeta
        },
        widgets: {
            bind: bindWidgets,
            emitChannel: emitWidgetChannel,
            render: renderWidgetHtml,
            refreshInstance: refreshWidgetInstance,
            refreshInstanceById: refreshWidgetInstanceById,
            loadData: loadWidgetData,
            renderArea: renderWidgetAreaHtml,
            refreshAreaCards: refreshWidgetAreaCards,
            refreshAreaRegions: refreshWidgetAreaRegions,
            refreshArea: refreshWidgetArea,
            refreshAreas: refreshWidgetAreas,
            action: runWidgetAction,
            handleActionResponse: handleWidgetActionResponse
        },
        forms: {
            bind: bindForms,
            submit: handleAjaxForm,
            validate: validateForm,
            clearErrors: clearFormErrors,
            setErrors: setFormErrors,
            getSchema: getFormSchema
        },
        realtime: {
            connect: connectRealtime,
            setStatus: setRealtimeStatus
        },
        ui: {
            toast: showToast,
            processCommands: processCommands
        },
        csrf: {
            token: getCsrfToken,
            headers: csrfHeaders
        },
        media: {},
        init: initialize
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
})(window, document, window.jQuery);
