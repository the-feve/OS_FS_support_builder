import math
def build(edge, hTop_lim, mode='COUNT', count=12, spacing=8, g=1.0, ov=0.15, bw=3, tw=0.6, foot=20, tW=3):
    step=max(1,hTop_lim/300); H=[];S=[]; h=step/2
    while h<=hTop_lim:
        s=edge(h)
        if s is None:
            if H: break
        else: H.append(h); S.append(s)
        h+=step
    last=len(H)-1; hS,hT=H[0],H[-1]
    lo,hi=hS+bw/2,hT-bw/2; teeth=[]
    if hi>=lo:
        mp=bw*1.1
        if mode=='COUNT':
            n=min(count,math.floor((hi-lo)/mp)+1); p=(hi-lo)/(n-1) if n>1 else 0
        else:
            p=max(spacing,mp); n=math.floor((hi-lo)/p)+1
        f=lo+((hi-lo)-p*(n-1))/2 if n>1 else (lo+hi)/2
        teeth=[f+p*k for k in range(n)]
    tp=lambda hk:[(edge(hk-bw/2)-g,hk-bw/2),(edge(hk-tw/2)+ov,hk-tw/2),(edge(hk+tw/2)+ov,hk+tw/2),(edge(hk+bw/2)-g,hk+bw/2)]
    pts=[(S[0]-g,0)];k=0
    for j in range(last+1):
        h=H[j]
        while k<len(teeth) and teeth[k]-bw/2<=h: pts+=tp(teeth[k]);k+=1
        if k>0 and h<=teeth[k-1]+bw/2: continue
        pts.append((S[j]-g,h))
    mi=min(s-g for s in S); fs=min(mi-foot,mi-tW); ot=S[-1]-g-tW
    oa=lambda h: fs+(ot-fs)*(h/hT)
    pts.append((ot,hT))
    for j in range(last-1,0,-1):
        if j%4: continue
        pts.append((min(oa(H[j]),S[j]-g-tW),H[j]))
    pts.append((fs,0))
    return pts,len(teeth)
from shapely.geometry import Polygon
cases={
 'leaning curved':lambda h: 40-0.35*h-0.002*h*h if h<=110 else None,
 'bulge':lambda h: 10+8*math.sin(h/15) if h<=90 else None,
 'floating base':lambda h: (5+0.2*h) if 12<h<100 else None,
}
for name,e in cases.items():
  for mode in ['COUNT','SPACING']:
    pts,n=build(e,80,mode)
    P=Polygon(pts)
    print(f"{name:15s} {mode:8s} teeth={n:3d} pts={len(pts):4d} valid={P.is_valid} area={P.area:.0f}")
pts,n=build(cases['leaning curved'],80,count=500); print('count500 ->',n,Polygon(pts).is_valid)
import matplotlib; matplotlib.use('Agg'); import matplotlib.pyplot as plt
fig,ax=plt.subplots(1,3,figsize=(12,5))
for a,(name,e) in zip(ax,cases.items()):
    pts,n=build(e,80); xs,ys=zip(*(pts+[pts[0]]))
    a.fill(xs,ys,color='#4a7',alpha=.6); a.plot(xs,ys,'k',lw=.6)
    hh=[h/2 for h in range(0,240)]; ee=[(e(h),h) for h in hh if e(h) is not None]
    a.plot([p[0] for p in ee],[p[1] for p in ee],'r',lw=2,label='part edge')
    a.axhline(0,color='gray'); a.set_aspect('equal'); a.set_title(f"{name} ({n} attach pts)"); a.legend(loc='upper right')
plt.tight_layout(); plt.savefig('/home/user/OS_FS_support_builder/docs/profile_preview.png',dpi=90)
