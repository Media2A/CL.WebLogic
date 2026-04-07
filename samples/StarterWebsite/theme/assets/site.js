(function ($) {
    "use strict";

    const client = window.WebLogicClient || {};

    const state = {
        lastRefresh: null,
        feed: [],
        signalRConnection: null,
        widgetCatalog: [],
        activeWidgetFilter: "all",
        widgetAreas: [],
        dashboardStudio: {
            dashboardKey: "main",
            ownerKey: "",
            layout: []
        }
    };

    function formatTime(date) {
        return date.toLocaleTimeString([], {
            hour: "2-digit",
            minute: "2-digit",
            second: "2-digit"
        });
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function pushFeed(kind, title, detail) {
        state.feed.unshift({
            kind,
            title,
            detail,
            time: formatTime(new Date())
        });

        state.feed = state.feed.slice(0, 6);
        renderFeed();
    }

    function renderFeed() {
        const $feed = $("[data-live-feed]");
        if (!$feed.length) {
            return;
        }

        const html = state.feed.length
            ? state.feed.map(item => `
                <li class="list-group-item d-flex gap-3 align-items-start">
                    <span class="status-dot mt-1 flex-shrink-0"></span>
                    <div class="flex-grow-1">
                        <div class="d-flex justify-content-between gap-3">
                            <strong>${escapeHtml(item.title)}</strong>
                            <span class="text-secondary small">${escapeHtml(item.time)}</span>
                        </div>
                        <div class="text-secondary-emphasis">${escapeHtml(item.detail)}</div>
                    </div>
                </li>`).join("")
            : `<li class="list-group-item text-secondary">Waiting for the first WebLogic pulse.</li>`;

        $feed.html(html);
    }

    function setMetric(selector, value) {
        const $target = $(selector);
        if ($target.length) {
            $target.text(value);
        }
    }

    function setList(selector, values) {
        const $target = $(selector);
        if (!$target.length) {
            return;
        }

        const items = values && values.length
            ? values.map(value => `<li class="list-group-item">${escapeHtml(value)}</li>`).join("")
            : `<li class="list-group-item text-secondary">No entries yet.</li>`;

        $target.html(items);
    }

    function renderFormResult(payload) {
        const $target = $("[data-profile-intake-result]");
        if (!$target.length) {
            return;
        }

        if (!payload) {
            $target.addClass("d-none").empty();
            return;
        }

        if (payload.success === false) {
            $target
                .removeClass("d-none")
                .html(`
                    <p class="fw-semibold text-danger mb-2">Validation failed</p>
                    <p class="mb-0 text-secondary">${escapeHtml(payload.message || "The form did not pass validation.")}</p>`);
            return;
        }

        const normalized = payload.normalized || {};
        const mapped = payload.mapped || {};
        const command = mapped.command || {};
        const record = mapped.record || {};
        const upload = payload.upload || null;
        const uploadHtml = upload
            ? `<li><strong>Upload</strong>: ${escapeHtml(upload.FileName || upload.fileName || "")} (${escapeHtml(String(upload.Length || upload.length || 0))} bytes, ${escapeHtml(upload.ContentType || upload.contentType || "")})</li>`
            : `<li><strong>Upload</strong>: no file uploaded</li>`;

        $target
            .removeClass("d-none")
            .html(`
                <p class="fw-semibold text-success mb-2">Server validation passed</p>
                <p class="mb-3 text-secondary">${escapeHtml(payload.message || "The form passed validation.")}</p>
                <ul class="mb-0">
                    <li><strong>Name</strong>: ${escapeHtml(normalized.displayName || "")}</li>
                    <li><strong>Email</strong>: ${escapeHtml(normalized.emailAddress || "")}</li>
                    <li><strong>Country</strong>: ${escapeHtml(normalized.country || "")}</li>
                    <li><strong>Local office</strong>: ${escapeHtml(normalized.localOffice || "")}</li>
                    <li><strong>Preferred mentor</strong>: ${escapeHtml(normalized.preferredMentorId || "")}</li>
                    <li><strong>Age</strong>: ${escapeHtml(String(normalized.age ?? ""))}</li>
                    <li><strong>Favorite color</strong>: ${escapeHtml(normalized.favoriteColor || "")}</li>
                    <li><strong>Bio</strong>: ${escapeHtml(normalized.bio || "")}</li>
                    <li><strong>Audience segment</strong>: ${escapeHtml(normalized.audienceSegment || "")}</li>
                    <li><strong>Form intent</strong>: ${escapeHtml(normalized.formIntent || "")}</li>
                    ${uploadHtml}
                </ul>
                <div class="mt-3">
                    <p class="fw-semibold mb-2">Mapped command</p>
                    <pre class="mb-0"><code>${escapeHtml(JSON.stringify(command, null, 2))}</code></pre>
                </div>
                <div class="mt-3">
                    <p class="fw-semibold mb-2">Mapped record</p>
                    <pre class="mb-0"><code>${escapeHtml(JSON.stringify(record, null, 2))}</code></pre>
                </div>`);
    }

    function showToast(title, detail) {
        if (client.ui && typeof client.ui.toast === "function") {
            client.ui.toast(title, detail);
            return;
        }
    }

    function normalizeList(values) {
        return (values || []).filter(function (value) {
            return !!value;
        });
    }

    function emitWidgetChannel(channel, payload) {
        if (client.widgets && typeof client.widgets.emitChannel === "function") {
            client.widgets.emitChannel(channel, payload);
            if (channel) {
                pushFeed("widget", `Widget channel: ${String(channel)}`, JSON.stringify(payload || {}));
            }
            return;
        }
    }

    function createPlacement(widgetName, zone, descriptor) {
        const sample = $.extend({}, (descriptor && descriptor.SampleParameters) || {});
        return {
            InstanceId: "",
            WidgetName: widgetName,
            Zone: zone || "main",
            Order: 0,
            Settings: sample
        };
    }

    function filterWidgets(widgets) {
        const filter = state.activeWidgetFilter || "all";
        if (filter === "all") {
            return widgets;
        }

        if (filter === "accessible") {
            return widgets.filter(widget => widget.AllowAnonymous || !widget.RequiredAccessGroups || !widget.RequiredAccessGroups.length);
        }

        return widgets.filter(widget => String(widget.SourceKind || "").toLowerCase() === filter.toLowerCase());
    }

    function renderWidgetCatalog(widgets) {
        const $list = $("[data-widget-list]");
        if (!$list.length) {
            return;
        }

        const filtered = filterWidgets(widgets);
        const items = filtered.length
            ? filtered.map(widget => `
                <li class="list-group-item">
                    <div class="d-flex justify-content-between gap-3">
                        <strong>${escapeHtml(widget.Name)}</strong>
                        <span class="text-secondary small">${escapeHtml(widget.SourceKind)}</span>
                    </div>
                    <div class="text-secondary-emphasis small">${escapeHtml(widget.Description || "No description")}</div>
                </li>`).join("")
            : `<li class="list-group-item text-secondary">No widgets matched the current filter.</li>`;

        $list.html(items);
        setMetric("[data-widget-count]", `${filtered.length} widgets`);
    }

    function loadWidgetPreview(widget) {
        const params = $.extend({}, widget.SampleParameters || {}, { name: widget.Name });
        return $.ajax({
            url: "/api/weblogic/widgets/render",
            method: "GET",
            data: params,
            dataType: "html"
        });
    }

    function renderWidgetGrid(widgets) {
        const $grid = $("[data-widget-grid]");
        if (!$grid.length) {
            return $.Deferred().resolve().promise();
        }

        const filtered = filterWidgets(widgets);
        if (!filtered.length) {
            $grid.html(`<div class="col-12"><div class="glass-card p-4 rounded-4 text-secondary">No widgets matched the current filter.</div></div>`);
            return $.Deferred().resolve().promise();
        }

        $grid.html(filtered.map(widget => `
            <div class="col-xl-6">
                <article class="demo-panel h-100">
                    <div class="section-header">
                        <div>
                            <h2 class="section-title">${escapeHtml(widget.Name)}</h2>
                            <p class="section-subtitle">${escapeHtml(widget.SourceName)} (${escapeHtml(widget.SourceKind)})</p>
                        </div>
                        <span class="badge rounded-pill badge-soft">${escapeHtml((widget.Tags || []).join(", "))}</span>
                    </div>
                    <div class="widget-preview-shell mt-3" data-widget-card="${escapeHtml(widget.Name)}">
                        <div class="glass-card p-4 rounded-4 text-secondary">Loading widget preview...</div>
                    </div>
                </article>
            </div>`).join(""));

        const jobs = filtered.map(widget =>
            loadWidgetPreview(widget)
                .done(function (html) {
                    $(`[data-widget-card="${CSS.escape(widget.Name)}"]`).html(html);
                })
                .fail(function (xhr) {
                    const reason = xhr.status === 401 || xhr.status === 403
                        ? "This widget is not accessible with the current session."
                        : "The widget preview failed to load.";
                    $(`[data-widget-card="${CSS.escape(widget.Name)}"]`).html(`<div class="glass-card p-4 rounded-4 text-secondary">${escapeHtml(reason)}</div>`);
                }));

        return $.when.apply($, jobs);
    }

    function refreshWidgetDashboard() {
        const $grid = $("[data-widget-grid]");
        const $list = $("[data-widget-list]");
        if (!$grid.length && !$list.length) {
            return $.Deferred().resolve().promise();
        }

        return $.getJSON("/api/weblogic/widgets")
            .done(function (response) {
                const widgets = (response.widgets || []).slice().sort(function (left, right) {
                    const leftKey = `${left.SourceKind}|${left.SourceName}|${left.Name}`.toLowerCase();
                    const rightKey = `${right.SourceKind}|${right.SourceName}|${right.Name}`.toLowerCase();
                    return leftKey.localeCompare(rightKey);
                });

                state.widgetCatalog = widgets;
                renderWidgetCatalog(widgets);
                renderWidgetGrid(widgets);
                pushFeed("widgets", "Widget dashboard refreshed", `Loaded ${widgets.length} widget definitions from WebLogic.`);
            })
            .fail(function () {
                if ($list.length) {
                    $list.html(`<li class="list-group-item text-secondary">Widget registry failed to load.</li>`);
                }
                if ($grid.length) {
                    $grid.html(`<div class="col-12"><div class="glass-card p-4 rounded-4 text-secondary">Widget previews failed to load.</div></div>`);
                }
            });
    }

    function refreshWidgetInstance($container) {
        if (client.widgets && typeof client.widgets.refreshInstance === "function") {
            return client.widgets.refreshInstance($container);
        }

        return $.Deferred().resolve().promise();
    }

    function refreshWidgetInstanceById(instanceId) {
        if (client.widgets && typeof client.widgets.refreshInstanceById === "function") {
            return client.widgets.refreshInstanceById(instanceId);
        }

        return $.Deferred().resolve().promise();
    }

    function loadWidgetData($container) {
        if (!client.widgets || typeof client.widgets.loadData !== "function") {
            return;
        }

        client.widgets.loadData($container).done(function (response) {
            const widgetName = $container.data("widget-name");
            const payload = response.payload || {};
            showToast(payload.title || widgetName, `Count: ${payload.count ?? "n/a"}`);
        }).fail(function () {
            showToast("Widget data failed", "Could not load widget data.");
        });
    }

    function renderWidgetAreas(areas) {
        const $list = $("[data-widget-area-list]");
        const $grid = $("[data-widget-area-grid]");
        if (!$list.length && !$grid.length) {
            return $.Deferred().resolve().promise();
        }

        const grouped = {};
        areas.forEach(function (area) {
            grouped[area.AreaName] = grouped[area.AreaName] || [];
            grouped[area.AreaName].push(area);
        });

        const names = Object.keys(grouped).sort();

        if ($list.length) {
            const items = names.length
                ? names.map(name => `<li class="list-group-item"><strong>${escapeHtml(name)}</strong><div class="small text-secondary">${escapeHtml(grouped[name].map(item => item.WidgetName).join(", "))}</div></li>`).join("")
                : `<li class="list-group-item text-secondary">No widget areas registered yet.</li>`;
            $list.html(items);
        }

        setMetric("[data-widget-area-count]", `${names.length} areas`);

        if (!$grid.length) {
            return $.Deferred().resolve().promise();
        }

        if (!names.length) {
            $grid.html(`<div class="col-12"><div class="glass-card p-4 rounded-4 text-secondary">No widget areas registered yet.</div></div>`);
            return $.Deferred().resolve().promise();
        }

        $grid.html(names.map(name => `
            <div class="col-xl-6">
                <article class="demo-panel h-100">
                    <div class="section-header">
                        <div>
                            <h2 class="section-title">${escapeHtml(name)}</h2>
                            <p class="section-subtitle">${escapeHtml(grouped[name].map(item => item.WidgetName).join(", "))}</p>
                        </div>
                        <span class="badge rounded-pill badge-soft">${grouped[name].length} widgets</span>
                    </div>
                    <div class="widget-preview-shell mt-3" data-widget-area-card="${escapeHtml(name)}">
                        <div class="glass-card p-4 rounded-4 text-secondary">Loading area preview...</div>
                    </div>
                </article>
            </div>`).join(""));

        const jobs = names.map(name =>
            loadWidgetAreaHtml(name, window.location.pathname)
                .done(function (html) {
                    $(`[data-widget-area-card="${CSS.escape(name)}"]`).html(html || `<div class="glass-card p-4 rounded-4 text-secondary">No accessible widgets in this area.</div>`);
                })
                .fail(function () {
                    $(`[data-widget-area-card="${CSS.escape(name)}"]`).html(`<div class="glass-card p-4 rounded-4 text-secondary">The area preview failed to load.</div>`);
                }));

        return $.when.apply($, jobs);
    }

    function refreshWidgetAreas() {
        const $list = $("[data-widget-area-list]");
        const $grid = $("[data-widget-area-grid]");
        if (!$list.length && !$grid.length) {
            return $.Deferred().resolve().promise();
        }

        return $.getJSON("/api/weblogic/widgetareas")
            .done(function (response) {
                const areas = response.areas || [];
                state.widgetAreas = areas;
                renderWidgetAreas(areas);
                pushFeed("areas", "Widget areas refreshed", `Loaded ${areas.length} area assignments from WebLogic.`);
            })
            .fail(function () {
                if ($list.length) {
                    $list.html(`<li class="list-group-item text-secondary">Widget areas failed to load.</li>`);
                }
                if ($grid.length) {
                    $grid.html(`<div class="col-12"><div class="glass-card p-4 rounded-4 text-secondary">Widget areas failed to load.</div></div>`);
                }
            });
    }

    function loadWidgetAreaHtml(name, targetPath) {
        if (client.widgets && typeof client.widgets.renderArea === "function") {
            return client.widgets.renderArea(name, targetPath);
        }

        return $.Deferred().resolve("").promise();
    }

    function refreshWidgetAreaCards(areaName) {
        if (client.widgets && typeof client.widgets.refreshAreaCards === "function") {
            return client.widgets.refreshAreaCards(areaName);
        }

        return $.Deferred().resolve().promise();
    }

    function refreshWidgetAreaRegions(areaName) {
        if (client.widgets && typeof client.widgets.refreshAreaRegions === "function") {
            return client.widgets.refreshAreaRegions(areaName);
        }

        return $.Deferred().resolve().promise();
    }

    function refreshWidgetAreaByName(areaName) {
        if (client.widgets && typeof client.widgets.refreshArea === "function") {
            return client.widgets.refreshArea(areaName);
        }

        return $.Deferred().resolve().promise();
    }

    function refreshWidgetAreasByNames(areaNames) {
        if (client.widgets && typeof client.widgets.refreshAreas === "function") {
            return client.widgets.refreshAreas(areaNames);
        }

        return $.Deferred().resolve().promise();
    }

    function normalizeDashboardLayout(layout) {
        const widgets = (layout || []).map(function (item, index) {
            return {
                InstanceId: item.InstanceId || item.instanceId || "",
                WidgetName: item.WidgetName || item.widgetName || "",
                Zone: (item.Zone || item.zone || "main").toLowerCase(),
                Order: item.Order || item.order || ((index + 1) * 10),
                Settings: $.extend({}, item.Settings || item.settings || {})
            };
        });

        ["main", "sidebar"].forEach(function (zone) {
            let order = 10;
            widgets.filter(function (item) { return item.Zone === zone; })
                .sort(function (left, right) { return left.Order - right.Order; })
                .forEach(function (item) {
                    item.Order = order;
                    order += 10;
                });
        });

        return widgets;
    }

    function getDashboardStudioLayout() {
        return normalizeDashboardLayout(state.dashboardStudio.layout);
    }

    function setDashboardStudioLayout(layout) {
        state.dashboardStudio.layout = normalizeDashboardLayout(layout);
        renderDashboardStudioEditor();
    }

    function getDashboardZoneWidgets(zone) {
        return getDashboardStudioLayout()
            .filter(function (item) { return item.Zone === zone; })
            .sort(function (left, right) { return left.Order - right.Order; });
    }

    function renderDashboardStudioCatalog() {
        const $catalog = $("[data-dashboard-widget-catalog]");
        if (!$catalog.length) {
            return;
        }

        const widgets = state.widgetCatalog.slice().sort(function (left, right) {
            const leftKey = `${left.SourceKind}|${left.SourceName}|${left.Name}`.toLowerCase();
            const rightKey = `${right.SourceKind}|${right.SourceName}|${right.Name}`.toLowerCase();
            return leftKey.localeCompare(rightKey);
        });

        setMetric("[data-dashboard-catalog-count]", `${widgets.length} widgets`);

        if (!widgets.length) {
            $catalog.html(`<div class="glass-card p-4 rounded-4 text-secondary">No widgets are available for this dashboard.</div>`);
            return;
        }

        $catalog.html(widgets.map(function (widget) {
            return `
                <article class="glass-card p-3 rounded-4">
                    <div class="d-flex justify-content-between gap-3 align-items-start">
                        <div>
                            <p class="mb-1 fw-semibold">${escapeHtml(widget.Name)}</p>
                            <p class="mb-2 small text-secondary">${escapeHtml(widget.Description || "No description")}</p>
                            <div class="small text-secondary">${escapeHtml(widget.SourceName)} (${escapeHtml(widget.SourceKind)})</div>
                        </div>
                        <span class="badge rounded-pill badge-soft">${escapeHtml((widget.Tags || []).join(", "))}</span>
                    </div>
                    <div class="d-flex gap-2 flex-wrap mt-3">
                        <button class="btn btn-sm btn-warning fw-semibold" type="button" data-dashboard-add="${escapeHtml(widget.Name)}" data-dashboard-zone="main">Add to main</button>
                        <button class="btn btn-sm btn-outline-light" type="button" data-dashboard-add="${escapeHtml(widget.Name)}" data-dashboard-zone="sidebar">Add to sidebar</button>
                    </div>
                </article>`;
        }).join(""));
    }

    function renderDashboardZoneEditor(zone, widgets) {
        const $zone = $(`[data-dashboard-zone-editor="${CSS.escape(zone)}"]`);
        if (!$zone.length) {
            return;
        }

        setMetric(`[data-dashboard-zone-count="${zone}"]`, widgets.length);

        if (!widgets.length) {
            $zone.html(`<div class="text-secondary">No widgets in the ${escapeHtml(zone)} zone yet.</div>`);
            return;
        }

        $zone.html(widgets.map(function (item, index) {
            const title = (item.Settings && item.Settings.title) || item.WidgetName;
            return `
                <article class="glass-card p-3 rounded-4" data-dashboard-item="${escapeHtml(zone)}:${index}">
                    <div class="d-flex justify-content-between gap-3 align-items-start">
                        <div>
                            <p class="mb-1 fw-semibold">${escapeHtml(item.WidgetName)}</p>
                            <p class="mb-0 small text-secondary">Instance: ${escapeHtml(item.InstanceId || "(new on save)")}</p>
                        </div>
                        <span class="badge rounded-pill badge-soft">${escapeHtml(zone)}</span>
                    </div>
                    <label class="form-label small text-uppercase text-secondary mt-3 mb-1">Title override</label>
                    <input class="form-control" type="text" data-dashboard-title="${zone}:${index}" value="${escapeHtml(title)}">
                    <div class="d-flex gap-2 flex-wrap mt-3">
                        <button class="btn btn-sm btn-outline-light" type="button" data-dashboard-move="up" data-dashboard-zone-name="${zone}" data-dashboard-index="${index}">Up</button>
                        <button class="btn btn-sm btn-outline-light" type="button" data-dashboard-move="down" data-dashboard-zone-name="${zone}" data-dashboard-index="${index}">Down</button>
                        <button class="btn btn-sm btn-outline-danger" type="button" data-dashboard-remove data-dashboard-zone-name="${zone}" data-dashboard-index="${index}">Remove</button>
                    </div>
                </article>`;
        }).join(""));
    }

    function renderDashboardStudioEditor() {
        renderDashboardZoneEditor("main", getDashboardZoneWidgets("main"));
        renderDashboardZoneEditor("sidebar", getDashboardZoneWidgets("sidebar"));
        renderDashboardStudioCatalog();
    }

    function refreshDashboardStudioPreview() {
        const $studio = $("[data-dashboard-studio]");
        if (!$studio.length) {
            return $.Deferred().resolve().promise();
        }

        const dashboardKey = state.dashboardStudio.dashboardKey || $studio.data("dashboard-key") || "main";
        const ownerKey = state.dashboardStudio.ownerKey || $studio.data("dashboard-owner-key") || "";
        const jobs = ["main", "sidebar"].map(function (zone) {
            return $.ajax({
                url: "/api/weblogic/dashboard/render",
                method: "GET",
                data: {
                    dashboardKey: dashboardKey,
                    ownerKey: ownerKey,
                    zone: zone
                },
                dataType: "html"
            }).done(function (html) {
                const fallback = `<div class="glass-card p-4 rounded-4 text-secondary">No widgets saved in the ${escapeHtml(zone)} zone yet.</div>`;
                $(`[data-dashboard-preview-zone="${CSS.escape(zone)}"]`).html(html || fallback);
            }).fail(function (xhr) {
                const message = xhr.status === 401
                    ? "Sign in to load your saved dashboard preview."
                    : `The ${zone} preview failed to load.`;
                $(`[data-dashboard-preview-zone="${CSS.escape(zone)}"]`).html(`<div class="glass-card p-4 rounded-4 text-secondary">${escapeHtml(message)}</div>`);
            });
        });

        return $.when.apply($, jobs);
    }

    function loadDashboardStudioLayout() {
        const $studio = $("[data-dashboard-studio]");
        if (!$studio.length) {
            return $.Deferred().resolve().promise();
        }

        const dashboardKey = $studio.data("dashboard-key") || "main";
        const ownerKey = $studio.data("dashboard-owner-key") || "";
        state.dashboardStudio.dashboardKey = dashboardKey;
        state.dashboardStudio.ownerKey = ownerKey;

        return $.getJSON("/api/weblogic/dashboard/layout", {
            dashboardKey: dashboardKey,
            ownerKey: ownerKey
        }).done(function (response) {
            setDashboardStudioLayout(response.widgets || []);
            refreshDashboardStudioPreview();
            pushFeed("dashboard", "Dashboard layout loaded", `Loaded ${(response.widgets || []).length} saved widgets for ${response.ownerKey || ownerKey || "current owner"}.`);
        }).fail(function (xhr) {
            const message = xhr.status === 401
                ? "Sign in to load your personal dashboard."
                : "Dashboard layout failed to load.";

            $("[data-dashboard-zone-editor='main']").html(`<div class="text-secondary">${escapeHtml(message)}</div>`);
            $("[data-dashboard-zone-editor='sidebar']").html(`<div class="text-secondary">${escapeHtml(message)}</div>`);
        });
    }

    function saveDashboardStudioLayout() {
        const $studio = $("[data-dashboard-studio]");
        if (!$studio.length) {
            return $.Deferred().resolve().promise();
        }

        const dashboardKey = state.dashboardStudio.dashboardKey || $studio.data("dashboard-key") || "main";
        const ownerKey = state.dashboardStudio.ownerKey || $studio.data("dashboard-owner-key") || "";
        const layout = getDashboardStudioLayout();

        return $.post("/api/weblogic/dashboard/layout/save", {
            dashboardKey: dashboardKey,
            ownerKey: ownerKey,
            layoutJson: JSON.stringify(layout)
        }).done(function (response) {
            setDashboardStudioLayout(response.widgets || []);
            refreshDashboardStudioPreview();
            handleWidgetActionResponse(response, null);
        }).fail(function () {
            showToast("Dashboard save failed", "Could not save your dashboard layout.");
        });
    }

    function updateDashboardTitle(zone, index, value) {
        const layout = getDashboardStudioLayout();
        const zoneWidgets = layout.filter(function (item) { return item.Zone === zone; }).sort(function (left, right) { return left.Order - right.Order; });
        if (!zoneWidgets[index]) {
            return;
        }

        zoneWidgets[index].Settings = $.extend({}, zoneWidgets[index].Settings || {}, {
            title: value || zoneWidgets[index].WidgetName
        });

        setDashboardStudioLayout(layout);
    }

    function moveDashboardWidget(zone, index, direction) {
        const layout = getDashboardStudioLayout();
        const zoneWidgets = layout
            .filter(function (item) { return item.Zone === zone; })
            .sort(function (left, right) { return left.Order - right.Order; });

        const targetIndex = direction === "up" ? index - 1 : index + 1;
        if (targetIndex < 0 || targetIndex >= zoneWidgets.length) {
            return;
        }

        const current = zoneWidgets[index];
        zoneWidgets[index] = zoneWidgets[targetIndex];
        zoneWidgets[targetIndex] = current;

        let order = 10;
        zoneWidgets.forEach(function (item) {
            item.Order = order;
            order += 10;
        });

        setDashboardStudioLayout(layout);
    }

    function removeDashboardWidget(zone, index) {
        const layout = getDashboardStudioLayout();
        const zoneWidgets = layout
            .filter(function (item) { return item.Zone === zone; })
            .sort(function (left, right) { return left.Order - right.Order; });

        if (!zoneWidgets[index]) {
            return;
        }

        const target = zoneWidgets[index];
        const filtered = layout.filter(function (item) {
            return item !== target;
        });

        setDashboardStudioLayout(filtered);
    }

    function addDashboardWidget(widgetName, zone) {
        const descriptor = state.widgetCatalog.find(function (item) {
            return String(item.Name || "").toLowerCase() === String(widgetName || "").toLowerCase();
        });

        if (!descriptor) {
            return;
        }

        const layout = getDashboardStudioLayout();
        layout.push(createPlacement(descriptor.Name, zone, descriptor));
        setDashboardStudioLayout(layout);
        showToast("Widget added", `${descriptor.Name} was added to the ${zone} zone.`);
    }

    function refreshDashboard() {
        const requests = {
            site: $.getJSON("/api/site"),
            routes: $.getJSON("/api/weblogic/routes"),
            plugins: $.getJSON("/api/weblogic/plugins"),
            apis: $.getJSON("/api/weblogic/apiexplorer"),
            events: $.getJSON("/api/weblogic/events/recent?take=6"),
            auth: $.getJSON("/api/weblogic/auth/me")
        };

        return $.when(requests.site, requests.routes, requests.plugins, requests.apis, requests.events, requests.auth)
            .done(function (siteResp, routesResp, pluginsResp, apisResp, eventsResp, authResp) {
                const site = siteResp[0];
                const routes = routesResp[0].routes || [];
                const contributors = pluginsResp[0].contributors || [];
                const apis = apisResp[0].apis || [];
                const events = eventsResp[0].events || [];
                const auth = authResp[0] || {};

                setMetric("[data-route-count]", routes.length);
                setMetric("[data-plugin-count]", contributors.length);
                setMetric("[data-api-count]", apis.length);
                setMetric("[data-site-title]", site.site || "Starter Website");
                setMetric("[data-site-tagline]", site.tagline || "");
                setMetric("[data-current-path]", site.pageContext?.Path || "/");
                setMetric("[data-current-method]", site.pageContext?.Method || "GET");
                setMetric("[data-current-user]", auth.userId || site.pageContext?.UserId || "anonymous");
                setMetric("[data-current-groups]", auth.accessGroups?.join(", ") || site.pageContext?.accessGroups?.join(", ") || "(none)");

                const topRoutes = routes.slice(0, 5).map(route => `${route.Path} (${route.Kind})`);
                const topPlugins = contributors.slice(0, 5).map(item => `${item.Name} - ${item.Kind}`);
                const topApis = apis.slice(0, 5).map(route => `${route.Path} [${route.Methods.join(", ")}]`);

                setList("[data-route-list]", topRoutes);
                setList("[data-plugin-list]", topPlugins);
                setList("[data-api-list]", topApis);
                syncFeedFromEvents(events);

                state.lastRefresh = new Date();
                pushFeed("sync", "Dashboard refreshed", `Loaded ${routes.length} routes, ${contributors.length} contributors, and ${apis.length} APIs.`);
                showToast("WebLogic dashboard synced", `Loaded ${routes.length} routes and ${contributors.length} contributors.`);
            })
            .fail(function () {
                pushFeed("error", "Refresh failed", "One or more demo endpoints could not be loaded.");
                showToast("Refresh failed", "A demo endpoint returned an error.");
            });
    }

    function syncFeedFromEvents(events) {
        if (!events || !events.length) {
            return;
        }

        state.feed = events.slice().reverse().map(event => ({
            kind: event.Kind || event.kind || "event",
            title: event.Title || event.title || event.Source || event.source || "WebLogic event",
            detail: event.Message || event.message || JSON.stringify(event.Payload || event.payload || {}),
            time: formatTime(new Date(event.TimestampUtc || event.timestampUtc || new Date()))
        })).slice(0, 6);

        renderFeed();
    }

    function setSignalRStatus(value) {
        if (client.realtime && typeof client.realtime.setStatus === "function") {
            client.realtime.setStatus(value);
            return;
        }

        const $target = $("[data-signalr-status]");
        if ($target.length) {
            $target.text(value);
        }
    }

    function setupSignalR() {
        if (!client.realtime || typeof client.realtime.connect !== "function") {
            setSignalRStatus("polling fallback");
            return;
        }

        client.on("realtime:event", function (payload) {
            const event = payload && payload.event ? payload.event : {};
            pushFeed(
                event.title || event.Title || "WebLogic event",
                event.source || event.Source || "cl.weblogic",
                event.message || event.Message || "Realtime event received."
            );
        });

        client.on("realtime:reconnected", function () {
            pushFeed("signalr", "Realtime reconnected", "SignalR connection restored.");
        });

        client.realtime.connect()
            .then(function (connection) {
                state.signalRConnection = connection;
                pushFeed("signalr", "Realtime connected", "SignalR is streaming WebLogic events.");
            })
            .catch(function () {
                setSignalRStatus("polling fallback");
            });
    }

    function bindAuthDemo() {
        const $form = $("[data-auth-demo-form]");
        if ($form.length) {
            $form.on("submit", function (event) {
                event.preventDefault();
                $.post("/api/weblogic/auth/demo-signin", $form.serialize())
                    .done(function (response) {
                        showToast("Signed in", `Session user is now ${response.userId}.`);
                        refreshDashboard();
                    })
                    .fail(function () {
                        showToast("Auth failed", "Could not sign in the demo user.");
                    });
            });
        }

        $("[data-auth-signout]").on("click", function () {
            $.post("/api/weblogic/auth/signout")
                .done(function () {
                    showToast("Signed out", "The demo session was cleared.");
                    refreshDashboard();
                })
                .fail(function () {
                    showToast("Sign-out failed", "Could not clear the demo session.");
                });
        });
    }

    function handleWidgetActionResponse(response, $widget) {
        if (client.widgets && typeof client.widgets.handleActionResponse === "function") {
            return client.widgets.handleActionResponse(response, {
                widget: $widget,
                onMessage: function (message) {
                    showToast(message.title, message.detail);
                    pushFeed("widget", message.title, message.detail);
                },
                onChannel: function (message) {
                    pushFeed("widget", `Widget channel: ${message.channel}`, JSON.stringify(message.payload || {}));
                },
                onRefreshWidgetAreas: function () {
                    return refreshWidgetAreas();
                },
                onRefreshWidgetCatalog: function () {
                    return refreshWidgetDashboard();
                },
                onRefreshDashboard: function () {
                    return refreshDashboard();
                }
            });
        }

        return $.Deferred().resolve().promise();
    }

    function bindInteractions() {
        $("[data-refresh-dashboard]").on("click", function (event) {
            event.preventDefault();
            refreshDashboard();
        });

        $("[data-widget-dashboard-refresh]").on("click", function (event) {
            event.preventDefault();
            refreshWidgetDashboard();
            refreshWidgetAreas();
        });

        $(document).on("click", "[data-widget-filter]", function () {
            state.activeWidgetFilter = $(this).data("widget-filter") || "all";
            $("[data-widget-filter]").removeClass("btn-warning fw-semibold").addClass("btn-outline-light");
            $(this).removeClass("btn-outline-light").addClass("btn-warning fw-semibold");
            renderWidgetCatalog(state.widgetCatalog);
            renderWidgetGrid(state.widgetCatalog);
        });

        $(document).on("click", "[data-widget-action]", function () {
            const $button = $(this);
            const $widget = $button.closest("[data-widget-instance]");
            const widgetName = $widget.data("widget-name");
            const instanceId = $widget.data("widget-instance");
            const action = $button.data("widget-action");

            if (!widgetName || !action) {
                return;
            }

            const request = {
                name: widgetName,
                instanceId: instanceId,
                action: action
            };

            const job = client.widgets && typeof client.widgets.action === "function"
                ? client.widgets.action(request)
                : $.post("/api/weblogic/widgets/action", request);

            job.done(function (response) {
                handleWidgetActionResponse(response, $widget);
                showToast("Widget action complete", `${widgetName} handled ${action}.`);
            }).fail(function () {
                showToast("Widget action failed", `Could not run ${action} for ${widgetName}.`);
            });
        });

        $(document).on("click", "[data-widget-load-data]", function () {
            const $widget = $(this).closest("[data-widget-instance]");
            loadWidgetData($widget);
        });

        $(document).on("click", "[data-dashboard-add]", function () {
            addDashboardWidget($(this).data("dashboard-add"), $(this).data("dashboard-zone") || "main");
        });

        $(document).on("input", "[data-dashboard-title]", function () {
            const raw = String($(this).data("dashboard-title") || "");
            const parts = raw.split(":");
            if (parts.length !== 2) {
                return;
            }

            updateDashboardTitle(parts[0], Number(parts[1]), $(this).val());
        });

        $(document).on("click", "[data-dashboard-move]", function () {
            moveDashboardWidget($(this).data("dashboard-zone-name"), Number($(this).data("dashboard-index")), $(this).data("dashboard-move"));
        });

        $(document).on("click", "[data-dashboard-remove]", function () {
            removeDashboardWidget($(this).data("dashboard-zone-name"), Number($(this).data("dashboard-index")));
        });

        $(document).on("click", "[data-dashboard-save]", function () {
            saveDashboardStudioLayout();
        });

        $(document).on("click", "[data-dashboard-reload]", function () {
            loadDashboardStudioLayout();
        });

        $("[data-scroll-to]").on("click", function () {
            const target = $(this).data("scroll-to");
            if (!target) {
                return;
            }

            const $target = $(target);
            if ($target.length) {
                $("html, body").animate({ scrollTop: $target.offset().top - 88 }, 320);
            }
        });

        $(document).on("weblogic:widget-channel", function (_, message) {
            const channel = message && message.channel ? String(message.channel) : "";
            if (!channel) {
                return;
            }

            $(`[data-widget-refresh-on="${CSS.escape(channel)}"]`).each(function () {
                const $target = $(this);

                if ($target.is("[data-widget-instance]")) {
                    refreshWidgetInstance($target);
                    return;
                }

                const areaName = $target.data("widget-area-region");
                if (areaName) {
                    refreshWidgetAreaByName(areaName);
                }
            });

            if (channel === "dashboard.layout.updated" && $("[data-dashboard-studio]").length) {
                loadDashboardStudioLayout();
            }
        });
    }

    $(function () {
        if (client.on) {
            client.on("forms:submit-complete", function (payload) {
                const form = payload && payload.form;
                const response = payload && payload.response;
                if (!form || !form.matches || !form.matches("[data-profile-intake-form]")) {
                    return;
                }

                renderFormResult(response);
                if (response && response.success) {
                    showToast("Form validated", response.message || "The form passed validation.");
                }
            });

            client.on("forms:submit-blocked", function (payload) {
                const form = payload && payload.form;
                if (!form || !form.matches || !form.matches("[data-profile-intake-form]")) {
                    return;
                }

                renderFormResult({
                    success: false,
                    message: "The browser validation blocked submit until the highlighted fields are fixed."
                });
            });

            client.on("widgets:channel", function (message) {
                if (!message || !message.channel) {
                    return;
                }

                pushFeed("widget", `Widget channel: ${message.channel}`, JSON.stringify(message.payload || {}));
            });
        }

        bindInteractions();
        bindAuthDemo();
        renderFeed();
        refreshDashboard();
        refreshWidgetDashboard();
        refreshWidgetAreas();
        loadDashboardStudioLayout();
        setupSignalR();

        setInterval(function () {
            if (document.hidden) {
                return;
            }

            refreshDashboard();
        }, 15000);

        setMetric("[data-current-year]", new Date().getFullYear());
    });
})(jQuery);
