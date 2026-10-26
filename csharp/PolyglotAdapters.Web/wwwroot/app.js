// Vanilla JS frontend: posts to the C# API, shows field validation errors and the summary.
const $ = (id) => document.getElementById(id);
$("f").addEventListener("submit", async (ev) => {
  ev.preventDefault(); $("msg").textContent = ""; $("out").hidden = true;
  const res = await fetch("/api/notes/summarize", { method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ sampleId: $("sampleId").value.trim(), text: $("text").value }) });
  const body = await res.json().catch(() => ({}));
  if (!res.ok) { $("msg").textContent = body.errors ? Object.values(body.errors).flat().join(" · ") : (body.detail || "Fel " + res.status); return; }
  $("out").textContent = `Prov ${body.sampleId} (jobb ${body.jobId})\n\n${body.summary}`; $("out").hidden = false;
});
