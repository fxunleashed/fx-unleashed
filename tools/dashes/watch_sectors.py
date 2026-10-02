import urllib.request, json, io, time, sys
from PIL import Image
N='DataCorePlugin.GameData.NewData.'
U='http://127.0.0.1:8899/api/props?names='+','.join(N+k for k in ('CurrentLapTime','CurrentSectorIndex','Sector1Time','Sector2Time','Sector3LastLapTime'))
pts={'S1':(406,300),'S2':(565,300),'S3':(724,300)}
out=open(sys.argv[1],'w'); prev=None; end=time.time()+240
while time.time()<end:
    try:
        d=json.load(urllib.request.urlopen(U,timeout=2))
        im=Image.open(io.BytesIO(urllib.request.urlopen('http://127.0.0.1:8899/api/wheel/frame.png',timeout=2).read())).convert('RGB')
        cols={k:'#%02X%02X%02X'%im.getpixel(p) for k,p in pts.items()}
        key=(d.get(N+'CurrentSectorIndex'),d.get(N+'Sector1Time'),d.get(N+'Sector2Time'),d.get(N+'Sector3LastLapTime'),tuple(cols.values()))
        if key!=prev:
            out.write(f"{d['_utc']} lap {d.get(N+'CurrentLapTime')} idx {key[0]} S1 {key[1]} S2 {key[2]} S3last {key[3]} | blocks {cols}\n"); out.flush(); prev=key
    except Exception as e:
        out.write(f'err {e}\n'); out.flush()
    time.sleep(0.25)
