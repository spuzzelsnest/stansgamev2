import collada, numpy as np, sys
d = collada.Collada(sys.argv[1], ignore=[collada.common.DaeUnsupportedError, collada.common.DaeBrokenRefError])
out = {}
for g in d.scene.objects('geometry'):
    for p in g.primitives():
        if not isinstance(p, collada.triangleset.BoundTriangleSet):
            try: p = p.triangleset()
            except Exception: continue
        m = p.material.id if p.material else 'none'
        V = p.vertex[p.vertex_index].reshape(-1, 3)
        if p.texcoordset and len(p.texcoordset):
            T = p.texcoordset[0][p.texcoord_indexset[0]].reshape(-1, 2)
        else: T = np.zeros((V.shape[0], 2))
        out.setdefault(m, []).append((V, T))
np.savez(sys.argv[2], **{k + '__V': np.vstack([a for a, _ in v]) for k, v in out.items()}, **{k + '__T': np.vstack([b for _, b in v]) for k, v in out.items()})
for k, v in out.items(): print(k, sum(len(a) for a, _ in v) // 3, 'tris')
