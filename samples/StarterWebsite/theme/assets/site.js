(function ($) {
    "use strict";

    const state = {
        lastRefresh: null,
        feed: [],
        signalRConnection: null
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

    function showToast(title, detail) {
        const $toast = $("#siteToast");
        if (!$toast.length || typeof bootstrap === "undefined") {
            return;
        }

        $("#siteToastTitle").text(title);
        $("#siteToastBody").text(detail);
        const toast = bootstrap.Toast.getOrCreateInstance($toast[0], { delay: 3000 });
        toast.show();
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
        const $target = $("[data-signalr-status]");
        if ($target.length) {
            $target.text(value);
        }
    }

    function setupSignalR() {
        if (typeof signalR === "undefined" || !signalR.HubConnectionBuilder) {
            setSignalRStatus("polling fallback");
            return;
        }

        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/weblogic-hubs/events")
            .withAutomaticReconnect()
            .build();

        connection.on("weblogic:event", function (event) {
            pushFeed(
                event.title || event.Title || "WebLogic event",
                event.source || event.Source || "cl.weblogic",
                event.message || event.Message || "Realtime event received."
            );
        });

        connection.onreconnecting(function () {
            setSignalRStatus("reconnecting");
        });

        connection.onreconnected(function () {
            setSignalRStatus("connected");
            pushFeed("signalr", "Realtime reconnected", "SignalR connection restored.");
        });

        connection.onclose(function () {
            setSignalRStatus("disconnected");
        });

        connection.start()
            .then(function () {
                state.signalRConnection = connection;
                setSignalRStatus("connected");
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

    function bindInteractions() {
        $("[data-refresh-dashboard]").on("click", function (event) {
            event.preventDefault();
            refreshDashboard();
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
    }

    $(function () {
        bindInteractions();
        bindAuthDemo();
        renderFeed();
        refreshDashboard();
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
