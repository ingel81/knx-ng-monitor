/* Tree pages: Group addresses (3-level, read/write/chart) + Topology (building tree). */
const { useState: uSt, useMemo: uMt } = React;

function TreeTools({ onExpand, onCollapse, filter, setFilter }) {
  return (
    <div className="tree-tools">
      {setFilter && (
        <div className="knx-field knx-field--search tree-search">
          <Icon name="search" size={16} className="field-icon" />
          <input className="knx-input knx-input--search" placeholder="Filter by address or name…" value={filter} onChange={e => setFilter(e.target.value)} />
          {filter && <button className="field-clear" onClick={() => setFilter('')}><Icon name="close" size={14} /></button>}
        </div>
      )}
      <div className="tree-tool-btns">
        <button className="knx-btn knx-btn--ghost knx-btn--sm" onClick={onExpand}><Icon name="expand" size={15} /> Expand all</button>
        <button className="knx-btn knx-btn--ghost knx-btn--sm" onClick={onCollapse}><Icon name="collapse" size={15} /> Collapse all</button>
      </div>
    </div>
  );
}

/* ---------------------------------------------------------------- GROUP ADDRESSES */
function GroupAddressesView({ vp, onChart }) {
  const tree = KNXData.GA_TREE;
  const [filter, setFilter] = uSt('');
  const allMidIds = uMt(() => { const s = []; tree.forEach(t => { s.push(t.id); t.children.forEach(c => s.push(c.id)); }); return s; }, []);
  const [open, setOpen] = uSt(() => new Set(tree.map(t => t.id)));
  const toggle = id => setOpen(o => { const n = new Set(o); n.has(id) ? n.delete(id) : n.add(id); return n; });
  const ql = filter.trim().toLowerCase();
  const matchGa = g => !ql || g[0].toLowerCase().includes(ql) || g[1].toLowerCase().includes(ql);
  const effectiveOpen = id => ql ? true : open.has(id);

  const count = node => node.children ? node.children.reduce((a, c) => a + c.gas.length, 0) : node.gas.length;

  return (
    <div className="page tree-page">
      <div className="toolbar tree-toolbar">
        <div className="tb-left"><h2 className="page-title">Group addresses</h2><span className="knx-badge knx-badge--count">{KNXData.GA_COUNT}</span></div>
        <TreeTools onExpand={() => setOpen(new Set(allMidIds))} onCollapse={() => setOpen(new Set())} filter={filter} setFilter={setFilter} />
      </div>
      <div className="page-scroll tree-scroll">
        {tree.map(top => {
          const topGas = top.children.flatMap(c => c.gas.filter(matchGa));
          if (ql && topGas.length === 0) return null;
          return (
            <div className="ga-top" key={top.id}>
              <button className="tree-row tree-row--top" onClick={() => toggle(top.id)}>
                <Icon name={effectiveOpen(top.id) ? 'chevronDown' : 'chevron'} size={15} className="tree-caret" />
                <Icon name="folder" size={17} className="tree-folder" />
                <span className="tree-id">{top.id}</span><span className="tree-name">{top.name}</span>
                <span className="knx-badge tree-count"><Icon name="share" size={11} /> {count(top)}</span>
              </button>
              {effectiveOpen(top.id) && top.children.map(mid => {
                const gas = mid.gas.filter(matchGa);
                if (ql && gas.length === 0) return null;
                return (
                  <div className="ga-mid" key={mid.id}>
                    <button className="tree-row tree-row--mid" onClick={() => toggle(mid.id)}>
                      <Icon name={effectiveOpen(mid.id) ? 'chevronDown' : 'chevron'} size={14} className="tree-caret" />
                      <Icon name="folder" size={15} className="tree-folder" />
                      <span className="tree-id">{mid.id}</span><span className="tree-name">{mid.name}</span>
                      <span className="knx-badge tree-count"><Icon name="share" size={10} /> {mid.gas.length}</span>
                    </button>
                    {effectiveOpen(mid.id) && gas.map(g => <GaRow key={g[0]} g={g} vp={vp} onChart={onChart} />)}
                  </div>
                );
              })}
            </div>
          );
        })}
      </div>
    </div>
  );
}

function GaRow({ g, vp, onChart }) {
  const [addr, name, dpt, kind] = g;
  const [val, setVal] = uSt('On');
  const isBool = kind === 'bool';
  return (
    <div className="ga-leaf">
      <div className="ga-leaf-info">
        <span className="ga-addr">{addr}</span>
        <span className="ga-name">{name}</span>
        <span className="ga-dpt">{dpt}</span>
      </div>
      <div className="ga-leaf-ctl">
        <button className="knx-btn knx-btn--outline knx-btn--sm"><Icon name="download" size={14} /> Read</button>
        {isBool
          ? <select className="knx-input ga-input" value={val} onChange={e => setVal(e.target.value)}><option>On</option><option>Off</option></select>
          : <input className="knx-input ga-input" placeholder="Value" />}
        <button className={`knx-btn knx-btn--sm ${isBool ? 'knx-btn--primary' : 'knx-btn--outline'}`} disabled={!isBool}><Icon name="upload" size={14} /> Write</button>
        <button className="knx-btn knx-btn--ghost knx-btn--sm ga-chart" onClick={() => onChart && onChart(addr)}><Icon name="chart" size={14} /> {vp !== 'mobile' && 'Chart'}</button>
      </div>
    </div>
  );
}

/* ---------------------------------------------------------------- TOPOLOGY */
const TYPE_LABEL_TOPO = { Building: 'BUILDING', BuildingPart: 'BUILDINGPART', Floor: 'FLOOR', Room: 'ROOM', DistributionBoard: 'DISTRIBUTIONBOARD' };

function collectIds(node, path, acc) {
  acc.push(path);
  (node.children || []).forEach((c, i) => collectIds(c, path + '/' + i, acc));
}

function TopologyView({ vp }) {
  const root = KNXData.TOPOLOGY;
  const allIds = uMt(() => { const a = []; collectIds(root, '0', a); return a; }, []);
  const [open, setOpen] = uSt(() => new Set(['0', '0/0', '0/0/0']));
  const toggle = id => setOpen(o => { const n = new Set(o); n.has(id) ? n.delete(id) : n.add(id); return n; });

  return (
    <div className="page tree-page">
      <div className="toolbar tree-toolbar">
        <div className="tb-left"><h2 className="page-title">Topology</h2><span className="knx-badge knx-badge--count">1</span></div>
        <TreeTools onExpand={() => setOpen(new Set(allIds))} onCollapse={() => setOpen(new Set(['0']))} />
      </div>
      <div className="page-scroll tree-scroll">
        <TopoNode node={root} path="0" depth={0} open={open} toggle={toggle} />
      </div>
    </div>
  );
}

function TopoNode({ node, path, depth, open, toggle }) {
  const hasChildren = node.children && node.children.length;
  const hasDevices = node.devices && node.devices.length;
  const isOpen = open.has(path);
  const isPlace = node.type === 'Room' || node.type === 'DistributionBoard';
  return (
    <div className={`topo-node ${depth ? 'is-child' : ''}`} style={{ marginLeft: depth ? 22 : 0 }}>
      <button className="tree-row topo-row" onClick={() => (hasChildren || hasDevices) && toggle(path)}>
        {(hasChildren || hasDevices)
          ? <Icon name={isOpen ? 'chevronDown' : 'chevron'} size={14} className="tree-caret" />
          : <span className="tree-caret-sp"></span>}
        <Icon name={isPlace ? 'mapPin' : 'folder'} size={16} className={isPlace ? 'topo-pin' : 'tree-folder'} />
        <span className="tree-name">{node.name}</span>
        <span className="topo-type">{TYPE_LABEL_TOPO[node.type]}</span>
        {hasDevices ? <span className="knx-badge tree-count"><Icon name="box" size={11} /> {node.devices.length}</span> : null}
      </button>
      {isOpen && hasDevices && (
        <div className="topo-devices" style={{ marginLeft: 22 }}>
          <span className="topo-dev-label">Devices</span>
          <div className="topo-dev-list">
            {node.devices.map((d, i) => <span className="device-chip" key={i}>{d}</span>)}
          </div>
        </div>
      )}
      {isOpen && hasChildren && node.children.map((c, i) => (
        <TopoNode key={i} node={c} path={path + '/' + i} depth={depth + 1} open={open} toggle={toggle} />
      ))}
    </div>
  );
}

Object.assign(window, { GroupAddressesView, TopologyView });
