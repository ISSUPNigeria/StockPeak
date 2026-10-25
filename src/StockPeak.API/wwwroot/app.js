const $ = s => document.querySelector(s), $$ = s => [...document.querySelectorAll(s)];
const esc = s => String(s ?? "").replace(/[&<>"\x27]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "\x27": "&#39;" }[c]));
const money = n => Number(n).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
let quotes = [], cur = null, detail = null, filter = "all", days = 120, hover = -1, user = null, watch = new Set();
let mode = "login", authEmail = "", inviteToken = null, tradeStatus = "", closing = null, view = "market";

async function api(path, opts = {}) {
  const t = localStorage.token;
  const res = await fetch("/api" + path, { ...opts, headers: { "Content-Type": "application/json", ...(t ? { Authorization: "Bearer " + t } : {}) }, body: opts.body ? JSON.stringify(opts.body) : undefined });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw Object.assign(new Error(data.error || (res.status === 429 ? "Too many attempts. Wait a minute and try again." : "Something went wrong. Try again.")), { status: res.status, code: data.code });
  return data;
}
function toast(msg) { const t = $("#toast"); t.textContent = msg; t.classList.add("show"); setTimeout(() => t.classList.remove("show"), 4000); }
const isStaff = () => ["Admin", "Manager"].includes(user?.role), isTeam = () => isStaff() || user?.role === "Agent";

// ---------- Views ----------
function show(v) {
  if (v !== "market" && !user) return openAuth("login");
  view = v; ["market", "journal", "team"].forEach(x => $("#v-" + x).hidden = x !== v);
  $$("#views button").forEach(b => b.classList.toggle("on", b.dataset.v === v));
  if (v === "journal") loadJournal(); if (v === "team") loadTeam(); if (v === "market") draw();
}
$("#views").onclick = e => e.target.dataset.v && show(e.target.dataset.v);

// ---------- How-to slider ----------
const SLIDES = [
  ["Pick a stock worth watching", "Open the scanner and filter by At peak or At bottom. Add the stocks you like to your watchlist and we will alert you when they move.", "Open the scanner", () => $("#q").focus()],
  ["Read the signal", "A stock near its recent high with an RSI above 70 may have peaked, which can be a time to sell. Near its low with an RSI below 30 it may have bottomed. A signal is a reason to look closer, not a promise.", "See peaks on the chart", () => $("#chart").scrollIntoView({ behavior: "smooth" })],
  ["Plan before you order", "Decide your entry price, how many shares to buy, where you will exit if you are wrong (your stop) and where you will take profit. Many traders risk no more than 1 to 2% of their account on a single trade.", "Try the position calculator", () => $("#shares").focus()],
  ["Place the order with your broker", "StockPeak does not execute trades. In your broker's app choose Buy or Sell and enter the number of shares. Then pick an order type: a market order fills now at the best price, a limit order fills only at your price or better, and a stop order triggers when the price reaches your level. Review the ticket, then confirm.", "Log this trade", () => show("journal")],
  ["Log it, then review it", "Record the trade in your journal when you open it. When you close it, write what you did, what went well and what you would change. Reviewing every trade is how you improve.", "Open my journal", () => show("journal")]
];
let slide = 0, timer = null;
$("#track").innerHTML = SLIDES.map(([t, p, b], i) => `<article class="slide" role="group" aria-roledescription="slide" aria-label="Step ${i + 1} of ${SLIDES.length}"><span class="n">${i + 1}</span><h2>${t}</h2><p>${p}</p><button data-i="${i}">${b}</button></article>`).join("");
$("#dots").innerHTML = SLIDES.map((_, i) => `<button data-d="${i}" aria-label="Go to step ${i + 1}"></button>`).join("");
function go(i) { slide = (i + SLIDES.length) % SLIDES.length; $("#track").style.transform = `translateX(-${slide * 100}%)`; $$("#dots button").forEach((d, k) => d.setAttribute("aria-current", k === slide)); }
function auto() { clearInterval(timer); if (!matchMedia("(prefers-reduced-motion: reduce)").matches) timer = setInterval(() => go(slide + 1), 9000); }
$("#prev").onclick = () => { go(slide - 1); auto(); }; $("#next").onclick = () => { go(slide + 1); auto(); };
$("#dots").onclick = e => { if (e.target.dataset.d) { go(+e.target.dataset.d); auto(); } };
$("#track").onclick = e => { const i = e.target.dataset.i; if (i !== undefined) SLIDES[i][3](); };
const sl = $("#learn"); sl.onmouseenter = sl.onfocusin = () => clearInterval(timer); sl.onmouseleave = sl.onfocusout = auto;
let x0 = null; sl.ontouchstart = e => x0 = e.touches[0].clientX; sl.ontouchend = e => { if (x0 === null) return; const d = e.changedTouches[0].clientX - x0; if (Math.abs(d) > 50) { go(slide + (d < 0 ? 1 : -1)); auto(); } x0 = null; };
sl.onkeydown = e => { if (e.key === "ArrowRight") $("#next").click(); if (e.key === "ArrowLeft") $("#prev").click(); };
go(0); auto();

// ---------- Scanner ----------
async function loadQuotes() { quotes = await api("/stocks"); renderList(); if (!cur) select(quotes[0].symbol); }
function renderList() {
  const q = $("#q").value.trim().toLowerCase();
  const rows = quotes.filter(x => (filter === "all" || (filter === "watch" ? watch.has(x.symbol) : x.signal === filter)) && (x.symbol + x.name).toLowerCase().includes(q));
  $("#list").innerHTML = rows.length ? rows.map(x => `<li data-s="${x.symbol}" class="${x.symbol === cur ? "on" : ""}">
    <span><b>${x.symbol}</b> ${x.signal !== "HOLD" ? `<span class="sig ${x.signal}">${x.signal}</span>` : ""}<br><small>${esc(x.name)}</small></span>
    <span style="text-align:right">${money(x.price)}<br><small class="${x.changePct >= 0 ? "up" : "down"}">${x.changePct >= 0 ? "+" : ""}${x.changePct}%</small></span></li>`).join("")
    : `<li><small>${filter === "watch" ? "Your watchlist is empty. Open a stock and add it." : "No stocks match."}</small></li>`;
}
$("#list").onclick = e => { const li = e.target.closest("li[data-s]"); if (li) select(li.dataset.s); };
$("#q").oninput = renderList;
$("#filters").onclick = e => { if (!e.target.dataset.f) return; filter = e.target.dataset.f; document.querySelectorAll("#filters button").forEach(b => b.classList.toggle("on", b === e.target)); renderList(); };
$("#range").onclick = e => { if (!e.target.dataset.d) return; days = +e.target.dataset.d; document.querySelectorAll("#range button").forEach(b => b.classList.toggle("on", b === e.target)); select(cur); };

// ---------- Detail + chart ----------
async function select(sym) {
  cur = sym; detail = await api(`/stocks/${sym}?days=${days}`); hover = -1;
  $("#sym").textContent = detail.symbol; $("#nm").textContent = detail.name; $("#px").textContent = money(detail.price);
  const s = $("#sig"); s.textContent = { PEAK: "At a peak", BOTTOM: "At a bottom", HOLD: "Hold" }[detail.signal]; s.className = "sig " + detail.signal;
  const last = detail.peaks.at(-1);
  $("#stats").innerHTML = [["RSI (14)", detail.rsi], ["Peaks found", detail.peaks.length], ["Last peak", last ? `${money(last.price)} on ${last.date.slice(0, 10)}` : "None yet"],
    ["Period high", money(Math.max(...detail.history.map(c => c.close)))]].map(([a, b]) => `<div class="stat"><span>${a}</span><b>${b}</b></div>`).join("");
  $("#aiOut").innerHTML = ""; $("#cost").value = detail.history[0].close; updateWatchBtn(); renderList(); draw(); pnl();
}
function draw() {
  if (!detail) return;
  const cv = $("#chart"), dpr = devicePixelRatio || 1, W = cv.clientWidth, H = cv.clientHeight; cv.width = W * dpr; cv.height = H * dpr;
  const g = cv.getContext("2d"); g.scale(dpr, dpr);
  const c = detail.history, v = c.map(x => x.close), lo = Math.min(...v) * .98, hi = Math.max(...v) * 1.02, P = 36;
  const X = i => P + i / (c.length - 1) * (W - P * 2), Y = p => H - 24 - (p - lo) / (hi - lo) * (H - 48);
  g.font = "12px Figtree"; g.fillStyle = "#5B6B7F"; g.strokeStyle = "#D3DCE5";
  for (let k = 0; k <= 4; k++) { const p = lo + (hi - lo) * k / 4, y = Y(p); g.beginPath(); g.moveTo(P, y); g.lineTo(W - P, y); g.stroke(); g.fillText(p.toFixed(0), 2, y + 4); }
  const grad = g.createLinearGradient(0, 0, 0, H); grad.addColorStop(0, "#1F4FD833"); grad.addColorStop(1, "#1F4FD800");
  g.beginPath(); v.forEach((p, i) => i ? g.lineTo(X(i), Y(p)) : g.moveTo(X(i), Y(p))); g.lineTo(X(v.length - 1), H - 24); g.lineTo(X(0), H - 24); g.fillStyle = grad; g.fill();
  g.beginPath(); v.forEach((p, i) => i ? g.lineTo(X(i), Y(p)) : g.moveTo(X(i), Y(p))); g.strokeStyle = "#1F4FD8"; g.lineWidth = 2; g.stroke();
  const dot = (pt, col, up) => { g.beginPath(); g.arc(X(pt.index), Y(pt.price), 6, 0, 7); g.fillStyle = col; g.fill(); g.strokeStyle = "#fff"; g.lineWidth = 2; g.stroke(); g.fillStyle = "#0F1B2D"; g.fillText(pt.price.toFixed(0), X(pt.index) - 10, Y(pt.price) + (up ? -12 : 20)); };
  detail.peaks.forEach(p => dot(p, "#E8A317", true)); detail.troughs.forEach(p => dot(p, "#1F4FD8", false));
  if (hover >= 0) { const x = X(hover), y = Y(v[hover]); g.strokeStyle = "#0F1B2D"; g.lineWidth = 1; g.beginPath(); g.moveTo(x, 8); g.lineTo(x, H - 24); g.stroke();
    const t = `${c[hover].date.slice(0, 10)}  ${money(v[hover])}`, w = g.measureText(t).width + 16; g.fillStyle = "#0F1B2D"; g.fillRect(Math.min(x, W - w - 4), 4, w, 24); g.fillStyle = "#fff"; g.fillText(t, Math.min(x, W - w - 4) + 8, 20); }
}
$("#chart").onmousemove = e => { if (!detail) return; const r = e.target.getBoundingClientRect(); hover = Math.max(0, Math.min(detail.history.length - 1, Math.round((e.clientX - r.left - 36) / (r.width - 72) * (detail.history.length - 1)))); draw(); };
$("#chart").onmouseleave = () => { hover = -1; draw(); };
addEventListener("resize", draw);

// ---------- Calculator ----------
function pnl() { const n = +$("#shares").value, c = +$("#cost").value; if (!detail || !n || !c) { $("#pnl").textContent = ""; return; }
  const d = (detail.price - c) * n, pct = (detail.price / c - 1) * 100; $("#pnl").innerHTML = `<b class="${d >= 0 ? "up" : "down"}">${d >= 0 ? "+" : ""}${money(d)} (${pct.toFixed(1)}%)</b> at today\x27s price`; }
$("#shares").oninput = $("#cost").oninput = pnl;

// ---------- Watchlist ----------
function updateWatchBtn() { $("#watch").textContent = watch.has(cur) ? "Remove from watchlist" : "Add to watchlist"; }
$("#watch").onclick = async () => {
  if (!user) return openAuth("login");
  try { if (watch.has(cur)) { await api("/watchlist/" + cur, { method: "DELETE" }); watch.delete(cur); toast(cur + " removed from watchlist"); }
        else { await api("/watchlist/" + cur, { method: "POST" }); watch.add(cur); toast(cur + " added. We will alert you at its next peak."); }
        updateWatchBtn(); renderList(); } catch (e) { toast(e.message); if (e.status === 402) openPlans(); }
};

// ---------- AI ----------
$("#aiBtn").onclick = async () => {
  if (!user) return openAuth("login");
  $("#aiOut").innerHTML = "<p>Asking Gemini...</p>";
  try { const p = await api(`/stocks/${cur}/predict`);
    $("#aiOut").innerHTML = `<p><b class="${p.direction === "up" ? "up" : p.direction === "down" ? "down" : ""}">${esc(p.direction)}</b> toward <b>${money(p.targetPrice)}</b> in 7 days (${p.confidence}% confidence)</p><p>${esc(p.reasoning)}</p><p class="fine">${esc(p.disclaimer)}</p>`;
  } catch (e) { $("#aiOut").innerHTML = `<p class="err">${esc(e.message)}</p>`; if (e.status === 402) openPlans(); }
};

// ---------- Auth: register, verify, login, forgot and reset ----------
const MODES = {
  login: { t: "Sign in", go: "Sign in", links: [["register", "Create an account"], ["forgot", "Forgot password?"]] },
  register: { t: "Create your account", go: "Create account", links: [["login", "I already have an account"]] },
  otp: { t: "Verify your email", go: "Confirm code", links: [["resend", "Send a new code"], ["login", "Back to sign in"]] },
  forgot: { t: "Reset your password", go: "Email me a code", links: [["login", "Back to sign in"]] },
  reset: { t: "Choose a new password", go: "Update password", links: [["forgot", "Send a new code"]] },
  invite: { t: "Accept your invitation", go: "Join workspace", links: [] }
};
function openAuth(m = "login", hint = "") {
  mode = m; $("#email").readOnly = m === "invite"; const c = MODES[m]; $("#authTitle").textContent = c.t; $("#authGo").textContent = c.go; $("#authErr").textContent = ""; $("#authHint").textContent = hint;
  $$("#authForm label[data-m]").forEach(l => l.hidden = !l.dataset.m.split(" ").includes(m));
  $("#links").innerHTML = c.links.map(([k, t]) => `<button type="button" class="ghost" data-k="${k}">${t}</button>`).join("");
  if (authEmail && !$("#email").value) $("#email").value = authEmail;
  if (!$("#authDlg").open) $("#authDlg").showModal();
}
$("#links").onclick = async e => { const k = e.target.dataset.k; if (!k) return;
  if (k === "resend") { try { await api("/auth/verify-email-otp", { method: "POST", body: { email: $("#email").value } }); toast("A new code is on its way."); } catch (x) { $("#authErr").textContent = x.message; } } else openAuth(k); };
async function setSession(r) { localStorage.token = r.token; $("#authDlg").close(); await boot(); }
$("#authForm").onsubmit = async e => {
  e.preventDefault(); const email = $("#email").value.trim(); authEmail = email; $("#authErr").textContent = "";
  try {
    if (mode === "login") await api("/auth/login", { method: "POST", body: { email, password: $("#pw").value } }).then(async r => { await setSession(r); toast("Signed in"); });
    else if (mode === "register") { await api("/auth/register", { method: "POST", body: { name: $("#name").value, email, password: $("#pw").value, role: $("#role").value, workspaceName: $("#wsname").value } }); openAuth("otp", `We emailed a 6-digit code to ${email}.`); }
    else if (mode === "otp") { const r = await api("/auth/confirm-email-otp", { method: "POST", body: { email, code: $("#code").value } }); await setSession(r); toast("Email verified. Welcome to StockPeak."); }
    else if (mode === "forgot") { await api("/auth/forgot-password", { method: "POST", body: { email } }); openAuth("reset", `If ${email} is registered, a code is on its way.`); }
    else if (mode === "reset") { await api("/auth/reset-password", { method: "POST", body: { email, code: $("#code").value, newPassword: $("#npw").value } }); openAuth("login", "Password updated. Sign in with your new password."); }
    else if (mode === "invite") { const r = await api("/invites/accept", { method: "POST", body: { token: inviteToken, name: $("#name").value, password: $("#pw").value } }); history.replaceState({}, "", "/"); await setSession(r); toast("You have joined the workspace."); }
  } catch (err) { if (err.code === "EMAIL_NOT_VERIFIED") openAuth("otp", "Verify your email to continue. We sent you a code."); else $("#authErr").textContent = err.message; }
};
$("#authBtn").onclick = () => { if (!user) return openAuth("login");
  localStorage.removeItem("token"); user = null; watch.clear(); renderUser(); renderList(); updateWatchBtn(); show("market"); toast("Signed out"); };
function renderUser() {
  $("#authBtn").textContent = user ? "Sign out" : "Sign in"; $("#bell").hidden = !user; $$("[data-auth]").forEach(b => b.hidden = !user);
  const ws = $("#ws"); ws.hidden = !user || user.workspaces.length < 2;
  if (user) ws.innerHTML = user.workspaces.map(w => `<option value="${w.id}" ${w.id === user.workspaceId ? "selected" : ""}>${esc(w.name)} (${w.role})</option>`).join("");
}
$("#ws").onchange = async e => { try { await setSession(await api(`/workspaces/${e.target.value}/switch`, { method: "POST" })); } catch (x) { toast(x.message); } };

// ---------- Invitations ----------
async function checkInvite() {
  inviteToken = new URLSearchParams(location.search).get("invite"); if (!inviteToken) return;
  try { const i = await api("/invites/preview?token=" + encodeURIComponent(inviteToken)); authEmail = i.email; openAuth("invite", `${i.workspace} invites you to join as ${i.role}.`);
    $("#email").value = i.email; $("#email").readOnly = true; $("#name").closest("label").hidden = i.hasAccount; $("#authHint").textContent += i.hasAccount ? " Enter your password to accept." : " Choose a password to create your account.";
  } catch (e) { toast(e.message); history.replaceState({}, "", "/"); }
}

// ---------- Journal ----------
const sideCls = s => s === "Buy" ? "BOTTOM" : "PEAK";
async function loadJournal() {
  $("#allRow").hidden = !isTeam();
  try {
    const [s, t] = await Promise.all([api("/trades/stats"), api(`/trades?status=${tradeStatus}${$("#allBox").checked ? "&all=true" : ""}`)]);
    $("#jstats").innerHTML = [["Total P&L", money(s.totalPnl), s.totalPnl >= 0 ? "up" : "down"], ["Win rate", s.winRate + "%"], ["Open", s.open], ["Closed", s.closed], ["Best trade", money(s.bestTrade), "up"], ["Worst trade", money(s.worstTrade), "down"]].map(([a, b, c]) => `<div class="stat2"><small>${a}</small><strong class="${c || ""}">${b}</strong></div>`).join("");
    window._trades = t;
    $("#trades").innerHTML = t.length ? t.map(tradeCard).join("") : `<p class="empty">No trades here yet. Log a trade above, and when you close it you will write what you did and what you learned.</p>`;
  } catch (e) { toast(e.message); }
}
function tradeCard(t) {
  const closed = !!t.closedAt, p = t.pnl, mine = true;
  const rv = [["What I did", t.whatIDid], ["What went well", t.whatWentWell], ["What I would change", t.whatToImprove]].filter(x => x[1]).map(([a, b]) => `<div><b>${a}</b>${esc(b)}</div>`).join("");
  return `<article class="trade"><div class="trow"><b>${esc(t.symbol)}</b><span class="sig ${sideCls(t.side)}">${t.side}</span>
    <span>${t.quantity} shares at ${money(t.entryPrice)}${closed ? ` to ${money(t.exitPrice)}` : ""}</span><span class="sp"></span>
    ${closed ? `<strong class="${p >= 0 ? "up" : "down"}">${p >= 0 ? "+" : ""}${money(p)}</strong>` : `<span class="sig">Open</span>`}
    <button class="mini" data-act="${closed ? "review" : "close"}" data-id="${t.id}">${closed ? "Edit review" : "Complete trade"}</button><button class="mini ghost" data-act="del" data-id="${t.id}">Delete</button></div>
    <small>${new Date(t.openedAt + "Z").toLocaleDateString()}${t.strategy ? " | " + esc(t.strategy) : ""}</small>${t.reason ? `<p>${esc(t.reason)}</p>` : ""}${rv ? `<div class="review">${rv}</div>` : ""}</article>`;
}
$("#tradeForm").onsubmit = async e => { e.preventDefault(); $("#tErr").textContent = "";
  try { await api("/trades", { method: "POST", body: { symbol: $("#tSym").value, side: $("#tSide").value, quantity: +$("#tQty").value, entryPrice: +$("#tEntry").value, fees: +$("#tFees").value || 0, strategy: $("#tStrat").value, reason: $("#tWhy").value } });
    e.target.reset(); toast("Trade logged"); loadJournal(); } catch (x) { $("#tErr").textContent = x.message; } };
$("#jfilter").onclick = e => { if (e.target.dataset.s === undefined) return; tradeStatus = e.target.dataset.s; $$("#jfilter button").forEach(b => b.classList.toggle("on", b === e.target)); loadJournal(); };
$("#allBox").onchange = loadJournal;
$("#exportBtn").onclick = async () => { const r = await fetch("/api/trades/export", { headers: { Authorization: "Bearer " + localStorage.token } }); const a = document.createElement("a"); a.href = URL.createObjectURL(await r.blob()); a.download = "stockpeak-journal.csv"; a.click(); };
$("#trades").onclick = async e => { const { act, id } = e.target.dataset; if (!act) return; const t = window._trades.find(x => x.id === id);
  if (act === "del") { if (confirm("Delete this trade from your journal?")) { await api("/trades/" + id, { method: "DELETE" }); loadJournal(); } return; }
  closing = t; const done = act === "review"; $("#exitRows").hidden = done; $("#closeTitle").textContent = done ? "Edit your review" : "Complete this trade"; $("#cGo").textContent = done ? "Save review" : "Complete trade";
  $("#closeSub").textContent = `${t.side} ${t.quantity} ${t.symbol} at ${money(t.entryPrice)}`; $("#cExit").value = ""; $("#cDid").value = t.whatIDid || ""; $("#cWell").value = t.whatWentWell || ""; $("#cBetter").value = t.whatToImprove || ""; $("#cErr").textContent = ""; $("#closeDlg").showModal(); };
$("#closeForm").onsubmit = async e => { e.preventDefault(); const rv = { whatIDid: $("#cDid").value, whatWentWell: $("#cWell").value, whatToImprove: $("#cBetter").value };
  try { if (closing.closedAt) await api(`/trades/${closing.id}/notes`, { method: "PUT", body: { strategy: closing.strategy, reason: closing.reason, ...rv } });
        else await api(`/trades/${closing.id}/close`, { method: "POST", body: { exitPrice: +$("#cExit").value, fees: +$("#cFees").value || 0, ...rv } });
    $("#closeDlg").close(); toast("Trade review saved"); loadJournal(); } catch (x) { $("#cErr").textContent = x.message; } };

// ---------- Team: members, roles, invites, workspaces ----------
const ASSIGNABLE = () => user.role === "Admin" ? ["User", "Merchant", "Agent", "Manager", "Admin"] : ["User", "Merchant", "Agent"];
async function loadTeam() {
  const w = user.workspaces.find(x => x.id === user.workspaceId); $("#wsName").textContent = `${w?.name || "Team"} (you are ${user.role})`;
  $("#inviteCard").hidden = !isStaff(); $("#iRole").innerHTML = isStaff() ? ASSIGNABLE().map(r => `<option>${r}</option>`).join("") : "";
  if (!isTeam()) { $("#members").innerHTML = `<p class="empty">Only agents, managers and admins can see the member list.</p>`; $("#overview").innerHTML = ""; return; }
  try {
    const m = await api("/workspaces/current/members");
    $("#members").innerHTML = `<table class="tbl">${m.map(x => `<tr><td>${esc(x.name)}<br><small>${esc(x.email)}</small></td><td>${isStaff() && x.id !== user.id ? `<select data-u="${x.id}" ${ASSIGNABLE().includes(x.role) ? "" : "disabled"}>${[...new Set([...ASSIGNABLE(), x.role])].map(r => `<option ${r === x.role ? "selected" : ""}>${r}</option>`).join("")}</select>` : x.role}</td><td>${isStaff() && x.id !== user.id && ASSIGNABLE().includes(x.role) ? `<button class="mini ghost" data-rm="${x.id}">Remove</button>` : ""}</td></tr>`).join("")}</table>`;
    if (isStaff()) {
      const o = await api("/workspaces/current/overview");
      $("#overview").innerHTML = [["Members", o.members], ["Open trades", o.openTrades], ["Closed trades", o.closedTrades], ["Workspace P&L", money(o.totalPnl), o.totalPnl >= 0 ? "up" : "down"]].map(([a, b, c]) => `<div class="stat2"><small>${a}</small><strong class="${c || ""}">${b}</strong></div>`).join("");
      const inv = await api("/invites"); $("#invites").innerHTML = inv.length ? `<h4>Pending</h4>` + inv.map(i => `<div class="stat"><span>${esc(i.email)} (${i.role})</span><button class="mini ghost" data-rv="${i.id}">Revoke</button></div>`).join("") : "";
    } else $("#overview").innerHTML = "";
  } catch (e) { toast(e.message); }
}
$("#members").onchange = async e => { if (!e.target.dataset.u) return; try { await api(`/workspaces/current/members/${e.target.dataset.u}/role`, { method: "PUT", body: { role: e.target.value } }); toast("Role updated"); } catch (x) { toast(x.message); loadTeam(); } };
$("#members").onclick = async e => { if (!e.target.dataset.rm || !confirm("Remove this person from the workspace?")) return; try { await api("/workspaces/current/members/" + e.target.dataset.rm, { method: "DELETE" }); loadTeam(); } catch (x) { toast(x.message); } };
$("#invites").onclick = async e => { if (e.target.dataset.rv) { await api("/invites/" + e.target.dataset.rv, { method: "DELETE" }); loadTeam(); } };
$("#inviteForm").onsubmit = async e => { e.preventDefault();
  try { const r = await api("/invites", { method: "POST", body: { email: $("#iEmail").value, role: $("#iRole").value } }); $("#inviteOut").innerHTML = `Invitation sent. You can also share this link: <a href="${esc(r.inviteLink)}">${esc(r.inviteLink)}</a>`; $("#iEmail").value = ""; loadTeam(); } catch (x) { $("#inviteOut").textContent = x.message; } };
$("#wsForm").onsubmit = async e => { e.preventDefault(); try { await setSession(await api("/workspaces", { method: "POST", body: { name: $("#wName").value } })); $("#wName").value = ""; toast("Workspace created. You are its admin."); } catch (x) { toast(x.message); } };

// ---------- Plans & Flutterwave checkout ----------
const PLANS = [["Free", 0, ["3 watchlist stocks", "Peak and bottom scanner", "Trade journal"]], ["Pro", 15, ["Unlimited watchlist", "Gemini AI forecasts", "Email peak alerts"]], ["Elite", 39, ["Everything in Pro", "Priority alerts", "Early access to new tools"]]];
function openPlans() {
  $("#plans").innerHTML = PLANS.map(([n, p, f]) => `<div class="plan ${n === "Pro" ? "hot" : ""}"><h3>${n}</h3><strong>$${p}<small>/mo</small></strong><ul>${f.map(x => `<li>${x}</li>`).join("")}</ul>${p ? `<button data-p="${n}">Subscribe to ${n}</button>` : `<button class="ghost" disabled>Included</button>`}</div>`).join("");
  $("#plansDlg").showModal();
}
$("#plansBtn").onclick = openPlans;
$("#plans").onclick = async e => { const p = e.target.dataset.p; if (!p) return; if (!user) { $("#plansDlg").close(); return openAuth("login"); }
  e.target.textContent = "Opening checkout..."; try { location.href = (await api("/billing/checkout", { method: "POST", body: { plan: p } })).link; } catch (err) { toast(err.message); e.target.textContent = "Subscribe to " + p; } };

// ---------- Alerts ----------
async function loadNotifs() { if (!user) return; const n = await api("/notifications"), unread = n.filter(x => !x.isRead).length; $("#badge").hidden = !unread; $("#badge").textContent = unread; window._n = n; }
$("#bell").onclick = async () => { const n = window._n || [];
  $("#notifs").innerHTML = n.length ? n.map(x => `<li class="${x.isRead ? "" : "new"}"><b>${esc(x.title)}</b><br>${esc(x.body)}<br><small>${new Date(x.createdAt + "Z").toLocaleString()}</small></li>`).join("") : "<li>No alerts yet. Add stocks to your watchlist to get peak alerts.</li>";
  $("#notifDlg").showModal(); await api("/notifications/read", { method: "POST" }); $("#badge").hidden = true; };
$$("dialog .close").forEach(b => b.onclick = () => b.closest("dialog").close());

// ---------- Boot ----------
async function boot() {
  user = null; watch = new Set();
  if (localStorage.token) { try { const me = await api("/auth/me"); localStorage.token = me.token; user = { ...me, id: JSON.parse(atob(me.token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/"))).sub, workspaces: me.workspaces.map(w => ({ id: w.id, name: w.name, role: w.role })), workspaceId: me.workspaceId }; watch = new Set(await api("/watchlist")); } catch { localStorage.removeItem("token"); user = null; } }
  renderUser(); renderList(); updateWatchBtn(); loadNotifs(); if (!user && view !== "market") show("market"); else if (view !== "market") show(view);
}
(async () => {
  await loadQuotes(); $("#symbols").innerHTML = quotes.map(q => `<option value="${q.symbol}">${esc(q.name)}</option>`).join("");
  await boot(); await checkInvite();
  const p = new URLSearchParams(location.search).get("paid");
  if (p) { toast(p === "1" ? "Payment confirmed. Your plan is active." : "Payment could not be confirmed. You have not been charged."); history.replaceState({}, "", "/"); }
  setInterval(loadNotifs, 30000); setInterval(loadQuotes, 60000);
})();
