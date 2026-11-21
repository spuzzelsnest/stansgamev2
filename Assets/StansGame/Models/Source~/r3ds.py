import struct
def read3ds(path):
    d=open(path,'rb').read(); objs=[]; mats=[]
    def walk(o,end,cur):
        while o<end:
            cid,ln=struct.unpack_from('<HI',d,o); body=o+6; nxt=o+ln
            if ln<6: break
            if cid in (0x4D4D,0x3D3D,0x4100,0xAFFF): 
                if cid==0x4100: walk(body,nxt,cur)
                elif cid==0xAFFF: mats.append({'name':None}); walk(body,nxt,mats[-1])
                else: walk(body,nxt,cur)
            elif cid==0x4000:
                e=d.index(b'\0',body); name=d[body:e].decode('latin1')
                ob={'name':name,'v':[],'f':[],'uv':[],'fm':[],'m':None}; objs.append(ob); walk(e+1,nxt,ob)
            elif cid==0x4110:
                n,=struct.unpack_from('<H',d,body); cur['v']=[struct.unpack_from('<3f',d,body+2+12*i) for i in range(n)]
            elif cid==0x4140:
                n,=struct.unpack_from('<H',d,body); cur['uv']=[struct.unpack_from('<2f',d,body+2+8*i) for i in range(n)]
            elif cid==0x4120:
                n,=struct.unpack_from('<H',d,body); cur['f']=[struct.unpack_from('<4H',d,body+2+8*i)[:3] for i in range(n)]
                walk(body+2+8*n,nxt,cur)
            elif cid==0x4130:
                e=d.index(b'\0',body); mn=d[body:e].decode('latin1'); k,=struct.unpack_from('<H',d,e+1)
                fs=struct.unpack_from(f'<{k}H',d,e+3); cur['fm'].append((mn,fs))
            elif cid==0x4160:
                cur['m']=struct.unpack_from('<12f',d,body)
            elif cid==0xA000:
                e=d.index(b'\0',body); cur['name']=d[body:e].decode('latin1')
            elif cid==0xA200: walk(body,nxt,cur)
            elif cid==0xA300:
                e=d.index(b'\0',body); cur['tex']=d[body:e].decode('latin1')
            o=nxt
    walk(0,len(d),None)
    return objs,mats
if __name__=='__main__':
    import sys
    objs,mats=read3ds(sys.argv[1])
    print(mats)
    for o in objs:
        if not o['v']: continue
        xs=[v[0] for v in o['v']];ys=[v[1] for v in o['v']];zs=[v[2] for v in o['v']]
        print(o['name'],len(o['v']),len(o['f']),len(o['uv']),[m for m,_ in o['fm']],'x',round(min(xs),2),round(max(xs),2),'y',round(min(ys),2),round(max(ys),2),'z',round(min(zs),2),round(max(zs),2))
