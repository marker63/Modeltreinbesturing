import re, sys, glob, os
from datetime import datetime
base='/mnt/user-data/uploads/Marco'
sets=[('Autolog/baanverkenner-log_2026-10-18_1325.txt','Autolog/hardwarelog_2026-10-18_1325.txt'),
      ('Autolog/baanverkenner-log_2026-10-18_1333.txt','Autolog/hardwarelog_2026-10-18_1333.txt'),
      ('Autolog/baanverkenner-log_2026-10-18_1349.txt','Autolog/hardwarelog_2026-10-18_1349.txt'),
      ('Autolog/baanverkenner-log_2026-10-18_1357.txt','Autolog/hardwarelog_2026-10-18_1357.txt'),
      ('baanverkenner-log_2026-10-18_1411.txt','hardwarelog_2026-10-18_1411.txt'),
      ('baanverkenner-log_2026-10-18_1424.txt','hardwarelog_2026-10-18_1424.txt'),
      ('baanverkenner-log_2026-10-18_1221.txt','hardwarelog_2026-10-18_1221.txt')]
def t(s): return datetime.strptime(s[:23],'%Y-%m-%d %H:%M:%S.%f')
tot=0; mis=[]
for vl,hl in sets:
    vp=os.path.join(base,vl); hp=os.path.join(base,hl)
    if not (os.path.exists(vp) and os.path.exists(hp)): print('ontbreekt',vl); continue
    hw=[]
    for l in open(hp,encoding='utf-8-sig'):
        m=re.match(r'(\S+ \S+)\s+Uit\s+\[Dinamo\] \S+\s+= wissel/sein (\d+) (afbuigend|rechtdoor)',l)
        if m: hw.append((t(m.group(1)),int(m.group(2)),m.group(3)))
        m=re.match(r'(\S+ \S+)\s+In\s+\[Dinamo\] \S+\s+= terugmelding wissel (\d+) (afbuigend|rechtdoor)',l)
        if m: hw.append((t(m.group(1)),int(m.group(2)),'TM-'+m.group(3)))
    for l in open(vp,encoding='utf-8-sig'):
        if not re.match(r'\d{4}-',l): continue
        ts=t(l)
        claims=[]
        m=re.search(r'Adres (\d+): (afbuigend|rechtdoor) \(wisselcommando',l)
        if m: claims.append((int(m.group(1)),m.group(2),'commando'))
        m=re.search(r'nadat wissel (\d+).*? naar (afbuigend|rechtdoor) is geschakeld',l)
        if m: claims.append((int(m.group(1)),m.group(2),'herstel'))
        m=re.search(r'Wissel (\d+) staat weer op (afbuigend)',l)
        if m: claims.append((int(m.group(1)),m.group(2),'partner'))
        m=re.search(r'klem op wissel (\d+) \((afbuigend)\)\. Die wissel gaat nu terug naar (rechtdoor)',l)
        if m: claims.append((int(m.group(1)),'rechtdoor','losmaken'))
        m=re.search(r'reed weer zonder kortsluiting nadat wissel (\d+) naar (afbuigend|rechtdoor)',l)
        if m: claims.append((int(m.group(1)),m.group(2),'klem'))
        for a,st,soort in claims:
            tot+=1
            # laatste Uit-commando voor deze wissel binnen 30 s voor de melding
            uit=[h for h in hw if h[1]==a and not h[2].startswith('TM') and 0<= (ts-h[0]).total_seconds() <=30]
            if soort in ('commando','losmaken'):
                uit=[h for h in hw if h[1]==a and not h[2].startswith('TM') and -1<= (h[0]-ts).total_seconds() <=3]
            if not uit:
                mis.append((vl,l.strip()[:200],'GEEN commando gevonden')); continue
            werkelijk=(uit[-1] if soort!='commando' else uit[0])[2]
            if werkelijk!=st: mis.append((vl,l.strip()[:200],f'commando was {werkelijk}'))
print('gecontroleerde meldingen:',tot)
for x in mis: print(x)
