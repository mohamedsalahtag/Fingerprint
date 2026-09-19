// Set up event handlers
const reconnectModal = document.getElementById("components-reconnect-modal");
reconnectModal.addEventListener("components-reconnect-state-changed", handleReconnectStateChanged);

const retryButton = document.getElementById("components-reconnect-button");
retryButton.addEventListener("click", retry);

const resumeButton = document.getElementById("components-resume-button");
resumeButton.addEventListener("click", resume);

function handleReconnectStateChanged(event) {
    if (event.detail.state === "show") {
        reconnectModal.showModal();
    } else if (event.detail.state === "hide") {
        reconnectModal.close();
    } else if (event.detail.state === "failed") {
        // Work out WHY we can't rejoin before nagging the user to retry: an expired sign-in
        // should take them to the login page, not leave them on "Failed to rejoin".
        handleLostConnection();
    } else if (event.detail.state === "rejected") {
        handleLostConnection();
    }
}

/// Ask the server whether we're still signed in.
///   not signed in -> session expired, go to the login page (remembering where we were)
///   signed in     -> server is fine, the circuit is just stale, so reload to carry on
///   unreachable   -> genuinely offline, keep the retry behaviour
async function handleLostConnection() {
    try {
        const res = await fetch("/auth/ping", { cache: "no-store", credentials: "same-origin" });
        if (res.ok) {
            const info = await res.json();
            if (info && info.authenticated === false) {
                goToLogin();
            } else {
                location.reload();
            }
            return;
        }
        if (res.status === 401 || res.status === 403) {
            goToLogin();
            return;
        }
    } catch {
        // server unreachable — fall through to the normal retry path
    }
    document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
}

function goToLogin() {
    const returnUrl = location.pathname + location.search;
    location.href = "/login?returnUrl=" + encodeURIComponent(returnUrl);
}

async function retry() {
    document.removeEventListener("visibilitychange", retryWhenDocumentBecomesVisible);

    try {
        // Reconnect will asynchronously return:
        // - true to mean success
        // - false to mean we reached the server, but it rejected the connection (e.g., unknown circuit ID)
        // - exception to mean we didn't reach the server (this can be sync or async)
        const successful = await Blazor.reconnect();
        if (!successful) {
            // We have been able to reach the server, but the circuit is no longer available.
            // We'll reload the page so the user can continue using the app as quickly as possible.
            const resumeSuccessful = await Blazor.resumeCircuit();
            if (!resumeSuccessful) {
                await handleLostConnection();
            } else {
                reconnectModal.close();
            }
        }
    } catch (err) {
        // We got an exception, server is currently unavailable
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    }
}

async function resume() {
    try {
        const successful = await Blazor.resumeCircuit();
        if (!successful) {
            await handleLostConnection();
        }
    } catch {
        reconnectModal.classList.replace("components-reconnect-paused", "components-reconnect-resume-failed");
    }
}

async function retryWhenDocumentBecomesVisible() {
    if (document.visibilityState === "visible") {
        await retry();
    }
}
