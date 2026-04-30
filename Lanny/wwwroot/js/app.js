"use strict";

let devices = [];
let currentFilter = "all";
let currentSort = { key: "lastSeen", dir: "desc" };
let searchQuery = "";

// --- SignalR Connection ---
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/devices")
    .withAutomaticReconnect()
    .build();

connection.on("DevicesUpdated", (data) => {
    devices = data;
    updateLastScan();
    render();
});

connection.onreconnecting(() => setConnectionStatus(false));
connection.onreconnected(() => setConnectionStatus(true));
connection.onclose(() => setConnectionStatus(false));

async function startConnection() {
    try {
        await connection.start();
        setConnectionStatus(true);
    } catch {
        setConnectionStatus(false);
        setTimeout(startConnection, 5000);
    }
}

function setConnectionStatus(connected) {
    const el = document.getElementById("connectionStatus");
    el.textContent = connected ? "Connected" : "Disconnected";
    el.className = "status-badge " + (connected ? "connected" : "disconnected");
}

function updateLastScan() {
    document.getElementById("lastScan").textContent =
        "Last update: " + new Date().toLocaleTimeString();
}

// --- Initial data load ---
async function loadDevices() {
    try {
        const resp = await fetch("/api/devices");
        if (resp.ok) {
            devices = await resp.json();
            render();
        }
    } catch { /* will get data via SignalR */ }
}

// --- Rendering ---
function render() {
    const filtered = devices
        .filter(d => {
            if (currentFilter === "online" && !d.isOnline) return false;
            if (currentFilter === "offline" && d.isOnline) return false;
            return true;
        })
        .filter(d => {
            if (!searchQuery) return true;
            const q = searchQuery.toLowerCase();
            return (
                (d.ipAddress || "").toLowerCase().includes(q) ||
                (d.macAddress || "").toLowerCase().includes(q) ||
                (d.hostname || "").toLowerCase().includes(q) ||
                (d.vendor || "").toLowerCase().includes(q) ||
                (d.systemName || "").toLowerCase().includes(q) ||
                (d.systemDescription || "").toLowerCase().includes(q) ||
                (d.httpTitle || "").toLowerCase().includes(q) ||
                (d.tlsCertificateSubject || "").toLowerCase().includes(q) ||
                (d.sshBanner || "").toLowerCase().includes(q)
            );
        })
        .sort((a, b) => {
            let va = a[currentSort.key] ?? "";
            let vb = b[currentSort.key] ?? "";
            if (typeof va === "boolean") { va = va ? 1 : 0; vb = vb ? 1 : 0; }
            if (typeof va === "string") { va = va.toLowerCase(); vb = vb.toLowerCase(); }
            let cmp = va < vb ? -1 : va > vb ? 1 : 0;
            return currentSort.dir === "desc" ? -cmp : cmp;
        });

    updateStats();
    renderTable(filtered);
}

function updateStats() {
    const total = devices.length;
    const online = devices.filter(d => d.isOnline).length;
    const offline = total - online;
    const vendors = new Set(devices.map(d => d.vendor).filter(Boolean)).size;

    document.getElementById("totalDevices").textContent = total;
    document.getElementById("onlineDevices").textContent = online;
    document.getElementById("offlineDevices").textContent = offline;
    document.getElementById("vendorCount").textContent = vendors;
    document.getElementById("deviceCount").textContent = `${total} device${total !== 1 ? "s" : ""}`;
}

function renderTable(data) {
    const tbody = document.getElementById("deviceTableBody");
    if (data.length === 0) {
        tbody.innerHTML = `<tr class="empty-row"><td colspan="8">No devices found</td></tr>`;
        return;
    }

    tbody.innerHTML = data.map(d => `
        <tr data-mac="${escapeHtml(d.macAddress)}">
            <td class="status-cell"><span class="status-dot ${d.isOnline ? "online" : "offline"}"></span></td>
            <td class="ip-address">${escapeHtml(d.ipAddress || "—")}</td>
            <td class="mac-address">${escapeHtml(d.macAddress || "—")}</td>
            <td class="hostname-cell">${escapeHtml(d.hostname || "—")}</td>
            <td class="vendor-cell">${escapeHtml(d.vendor || "Unknown")}</td>
            <td class="discovery-cell">${renderDiscoveryMethods(d.discoveryMethod)}</td>
            <td class="date-cell first-seen-cell">${formatDateCompact(d.firstSeen)}</td>
            <td class="date-cell last-seen-cell">${formatDateCompact(d.lastSeen)}</td>
        </tr>
    `).join("");
}

function renderDiscoveryMethods(methods) {
    if (!methods) return "—";
    return methods.split(",").map(m =>
        `<span class="discovery-tag">${escapeHtml(m.trim())}</span>`
    ).join("");
}

function formatDate(iso) {
    if (!iso) return "—";
    const d = new Date(iso);
    return d.toLocaleDateString() + " " + d.toLocaleTimeString();
}

function formatDateCompact(iso) {
    if (!iso) return "—";
    const d = new Date(iso);
    const now = new Date();
    const time = d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
    if (d.toDateString() === now.toDateString()) return time;
    const date = d.toLocaleDateString([], { month: "numeric", day: "numeric" }) +
        (d.getFullYear() === now.getFullYear() ? "" : "/" + String(d.getFullYear()).slice(2));
    return `${date} ${time}`;
}

function escapeHtml(str) {
    if (!str) return "";
    const div = document.createElement("div");
    div.textContent = str;
    return div.innerHTML;
}

function formatUptime(ticks) {
    if (ticks === null || ticks === undefined) return "";
    const totalSeconds = Math.floor(Number(ticks) / 100);
    const days = Math.floor(totalSeconds / 86400);
    const hours = Math.floor((totalSeconds % 86400) / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;
    return `${days}d ${hours}h ${minutes}m ${seconds}s`;
}

function renderOptionalDetailRow(label, value) {
    if (value === null || value === undefined || value === "") return "";
    return `
        <div class="detail-row">
            <span class="detail-label">${escapeHtml(label)}</span>
            <span class="detail-value">${escapeHtml(String(value))}</span>
        </div>
    `;
}

function renderOptionalHeaderRows(headers) {
    if (!headers || Object.keys(headers).length === 0) return "";
    const formattedHeaders = Object.entries(headers)
        .map(([key, value]) => `${key}: ${value}`)
        .join("\n");

    return renderOptionalDetailRow("HTTP Headers", formattedHeaders);
}

function formatTlsSans(subjectAlternativeNames) {
    if (!subjectAlternativeNames || subjectAlternativeNames.length === 0) return "";
    return subjectAlternativeNames.join(", ");
}

// --- Modal ---
function showDeviceModal(mac) {
    const d = devices.find(x => x.macAddress === mac);
    if (!d) return;

    document.getElementById("modalTitle").textContent = d.hostname || d.macAddress;
    document.getElementById("modalBody").innerHTML = `
        <div class="detail-row">
            <span class="detail-label">Status</span>
            <span class="detail-value"><span class="status-dot ${d.isOnline ? "online" : "offline"}"></span> ${d.isOnline ? "Online" : "Offline"}</span>
        </div>
        <div class="detail-row">
            <span class="detail-label">IP Address</span>
            <span class="detail-value">${escapeHtml(d.ipAddress || "—")}</span>
        </div>
        <div class="detail-row">
            <span class="detail-label">MAC Address</span>
            <span class="detail-value mac-address">${escapeHtml(d.macAddress)}</span>
        </div>
        <div class="detail-row">
            <span class="detail-label">Hostname</span>
            <span class="detail-value">${escapeHtml(d.hostname || "—")}</span>
        </div>
        <div class="detail-row">
            <span class="detail-label">Vendor</span>
            <span class="detail-value">${escapeHtml(d.vendor || "Unknown")}</span>
        </div>
        <div class="detail-row">
            <span class="detail-label">Discovery Methods</span>
            <span class="detail-value">${renderDiscoveryMethods(d.discoveryMethod)}</span>
        </div>
        ${renderOptionalDetailRow("SNMP System Name", d.systemName)}
        ${renderOptionalDetailRow("SNMP Description", d.systemDescription)}
        ${renderOptionalDetailRow("SNMP Object ID", d.systemObjectId)}
        ${renderOptionalDetailRow("SNMP Uptime", formatUptime(d.systemUptime))}
        ${renderOptionalDetailRow("SNMP Interfaces", d.interfaceCount)}
        ${renderOptionalDetailRow("HTTP Title", d.httpTitle)}
        ${renderOptionalHeaderRows(d.httpHeaders)}
        ${renderOptionalDetailRow("TLS Subject", d.tlsCertificateSubject)}
        ${renderOptionalDetailRow("TLS SANs", formatTlsSans(d.tlsSubjectAlternativeNames))}
        ${renderOptionalDetailRow("SSH Banner", d.sshBanner)}
        <div class="detail-row">
            <span class="detail-label">Open Ports</span>
            <span class="detail-value">${d.openPorts && d.openPorts.length ? d.openPorts.join(", ") : "—"}</span>
        </div>
        <div class="detail-row">
            <span class="detail-label">First Seen</span>
            <span class="detail-value">${formatDate(d.firstSeen)}</span>
        </div>
        <div class="detail-row">
            <span class="detail-label">Last Seen</span>
            <span class="detail-value">${formatDate(d.lastSeen)}</span>
        </div>
    `;
    document.getElementById("deviceModal").classList.remove("hidden");
}

function closeModal() {
    document.getElementById("deviceModal").classList.add("hidden");
}

// --- Event Listeners ---
document.addEventListener("DOMContentLoaded", () => {
    loadDevices();
    startConnection();

    // Search
    document.getElementById("searchInput").addEventListener("input", (e) => {
        searchQuery = e.target.value;
        render();
    });

    // Filters
    document.querySelectorAll(".filter-btn").forEach(btn => {
        btn.addEventListener("click", () => {
            document.querySelectorAll(".filter-btn").forEach(b => b.classList.remove("active"));
            btn.classList.add("active");
            currentFilter = btn.dataset.filter;
            render();
        });
    });

    // Sorting
    document.querySelectorAll("th.sortable").forEach(th => {
        th.addEventListener("click", () => {
            const key = th.dataset.sort;
            if (currentSort.key === key) {
                currentSort.dir = currentSort.dir === "asc" ? "desc" : "asc";
            } else {
                currentSort = { key, dir: "asc" };
            }
            document.querySelectorAll("th.sortable").forEach(h => h.classList.remove("sorted-asc", "sorted-desc"));
            th.classList.add(currentSort.dir === "asc" ? "sorted-asc" : "sorted-desc");
            render();
        });
    });

    // Row click -> modal
    document.getElementById("deviceTableBody").addEventListener("click", (e) => {
        const row = e.target.closest("tr");
        if (row && row.dataset.mac) showDeviceModal(row.dataset.mac);
    });

    // Modal close
    document.querySelector(".modal-close").addEventListener("click", closeModal);
    document.querySelector(".modal-backdrop").addEventListener("click", closeModal);
    document.addEventListener("keydown", (e) => { if (e.key === "Escape") closeModal(); });
});
