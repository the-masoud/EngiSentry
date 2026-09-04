(() => {
  const data = window.ENGISENTRY_PREVIEW;
  if (!data) return;
  const $ = (id) => document.getElementById(id);
  const nf = new Intl.NumberFormat('en-US');
  const esc = (value) => String(value ?? '—').replace(/[&<>'"]/g, (c) => ({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[c]));
  const metrics = [
    ['Rules', data.qaStats.RuleCount, 'accent'], ['Total findings', data.qaStats.TotalFindings, ''],
    ['Warnings', data.qaStats.WarningFindings, ''], ['Info', data.qaStats.InfoFindings, ''], ['Errors', data.qaStats.ErrorFindings, ''], ['AutoCAD', '2021', 'accent']
  ];
  $('drawingName').textContent = data.drawing;
  $('overallStatus').textContent = data.overall.replace('_', ' ');
  $('executionStatus').textContent = `${data.execution} · local artifact-derived report`;
  $('overviewMetrics').innerHTML = metrics.map(([label, value, cls]) => `<article class="metric panel ${cls}"><span>${esc(label)}</span><strong>${typeof value === 'number' ? nf.format(value) : esc(value)}</strong></article>`).join('');

  const placement = data.placementStats;
  const total = placement.EquipmentPlacementCount;
  $('placementBars').innerHTML = [['Complete',placement.CompletePlacementCount,'complete'],['Incomplete',placement.IncompletePlacementCount,'incomplete'],['Ambiguous',placement.AmbiguousPlacementCount,'ambiguous']].map(([label,count,cls]) => `<div class="bar-row"><span>${label}</span><div class="bar-track"><div class="bar-fill ${cls}" style="width:${count / total * 100}%"></div></div><b>${count}</b></div>`).join('');
  const geometry = data.geometryStats;
  $('geometryNumbers').innerHTML = [['Supported primitives',geometry.SupportedPrimitiveCount],['Geometry associations',geometry.GeometryAssociationCount],['Resolved associations',geometry.ResolvedAssociationCount],['Association findings',geometry.MissingAssociationCount + geometry.AmbiguousAssociationCount]].map(([label,value]) => `<div><b>${nf.format(value)}</b><span>${label}</span></div>`).join('');

  $('spatialSummary').innerHTML = [['Constraints',geometry.SpatialConstraintCount,''],['Resolved',geometry.ResolvedSpatialConstraintCount,''],['Missing',geometry.MissingSpatialConstraintCount,''],['Ambiguous',geometry.AmbiguousSpatialConstraintCount,'']].map(([label,value]) => `<div><strong>${value}</strong><span>${label}</span></div>`).join('');
  const wanted = ['UNIT LIMIT N. 1444000','UNIT LIMIT E. 1416000','ROAD C.L. E 1509500'];
  $('spatialExamples').innerHTML = wanted.map((text) => data.spatial.find((x) => x.LabelRawText === text)).filter(Boolean).map((x) => `<div class="spatial-example ${x.Status.toLowerCase()}"><div><b>${esc(x.LabelRawText)}</b><br><small>${esc(x.Axis)} = ${nf.format(x.CoordinateValue)} · ${esc(x.Status)}</small></div><small>${x.SelectedGeometryHandle ? `segment ${x.SelectedSegmentIndex} · ${esc(x.SelectedGeometryHandle)}` : esc(x.Reason)}</small></div>`).join('');

  $('ruleList').innerHTML = data.rules.map((rule) => `<div class="rule ${rule.count ? 'has-findings' : ''}"><b>${esc(rule.id)}</b><span>${rule.count ? `${rule.count} finding${rule.count === 1 ? '' : 's'}` : 'Pass'}</span></div>`).join('');
  const ruleSelect = $('ruleFilter'), categorySelect = $('categoryFilter');
  [...new Set(data.findings.map((x) => x.rule))].sort().forEach((value) => ruleSelect.insertAdjacentHTML('beforeend', `<option value="${esc(value)}">${esc(value)}</option>`));
  [...new Set(data.findings.map((x) => x.category))].sort().forEach((value) => categorySelect.insertAdjacentHTML('beforeend', `<option value="${esc(value)}">${esc(value)}</option>`));
  let severity = 'All';
  const renderFindings = () => {
    const rows = data.findings.filter((finding) => (severity === 'All' || finding.severity === severity) && (!ruleSelect.value || finding.rule === ruleSelect.value) && (!categorySelect.value || finding.category === categorySelect.value));
    $('findingCount').textContent = `${rows.length} of ${data.findings.length} findings`;
    $('findingsBody').innerHTML = rows.length ? rows.map((finding) => `<tr><td><span class="severity ${finding.severity.toLowerCase()}">${esc(finding.severity)}</span></td><td><b>${esc(finding.rule)}</b></td><td>${esc(finding.category)}</td><td>${esc(finding.subject)}</td><td>${esc(finding.message)}</td><td>${finding.reason ? esc(finding.reason) : ''}${finding.handles?.length ? `<span class="handles">${finding.handles.map(esc).join(' · ')}</span>` : ''}</td></tr>`).join('') : '<tr><td colspan="6" class="empty">No findings match these filters.</td></tr>';
  };
  document.querySelectorAll('[data-severity]').forEach((button) => button.addEventListener('click', () => { severity = button.dataset.severity; document.querySelectorAll('[data-severity]').forEach((x) => x.classList.toggle('active', x === button)); renderFindings(); }));
  ruleSelect.addEventListener('change', renderFindings); categorySelect.addEventListener('change', renderFindings); renderFindings();
  $('provenance').textContent = `Validated source: ${data.provenance.sourceArtifact} · ${data.provenance.commit.slice(0, 7)}`;
})();
