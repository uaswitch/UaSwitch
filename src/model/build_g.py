# Будує src/g.txt.gz — ключі для «ґ» (гудзик → ґудзик). Джерело: hunspell spell-uk (MPL 1.1), як і для апострофа;
# частотний словник (wordfreq) захищає імена й слова, які в живих текстах пишуть через «г».
# Запуск: unmunch uk_UA.dic uk_UA.aff > forms.txt; python3 build_g.py
import re, bisect, gzip
ok=re.compile(r"^[а-щьюяєіїґ'\-]+$")
words=set()
for l in open('forms.txt',encoding='utf8'):
    w=l.strip().split('/')[0].lower()
    if ok.match(w): words.add(w)
g_words={w for w in words if 'ґ' in w}
uk=[l.strip() for l in gzip.open('/home/claude/repo/src/uk.txt.gz','rt',encoding='utf8')]
rank={w:i for i,w in enumerate(uk)}
excluded=[]
for c in list(g_words):
    if c.count('ґ')!=1: continue
    w=c.replace('ґ','г')
    if w in rank and (c not in rank or rank[w] * 20 < rank[c]):
        words.add(w); excluded.append((w,rank[w],rank.get(c)))
print('виключено частотою:',len(excluded),sorted(excluded,key=lambda x:x[1])[:40])
plain=sorted(words)
def has_prefix(p):
    i=bisect.bisect_left(plain,p); return i<len(plain) and plain[i].startswith(p)
keys=set(); multi=0
for c in g_words:
    if c.count('ґ')!=1: multi+=1; continue
    i=c.index('ґ'); L,R=c[:i],c[i+1:]
    if c.replace('ґ','г') in words: continue  # обидва варіанти правильні (гніт/ґніт?) — не чіпаємо
    for k in range(0,len(R)+1):
        if not has_prefix(L+'г'+R[:k]): keys.add(L+'ґ'+R[:k]); break
    else: keys.add(c+'$')
out=sorted(keys)
open('/home/claude/repo/src/g.txt.gz','wb').write(gzip.compress("\n".join(out).encode(),9))
def fix(w):
    for i,ch in enumerate(w):
        if ch!='г': continue
        left=w[:i]+'ґ'
        for k in range(0,len(w)-i):
            if left+w[i+1:i+1+k] in keys: return left+w[i+1:]
        if left+w[i+1:]+'$' in keys: return left+w[i+1:]
fp=[w for w in words if 'г' in w and fix(w)]
tot=miss=0
for c in g_words:
    if c.count('ґ')!=1 or c.replace('ґ','г') in words: continue
    tot+=1; miss+= fix(c.replace('ґ','г'))!=c
print(f"слів з ґ: {len(g_words)} (з кількома ґ: {multi}), ключів {len(out)}, хибних {len(fp)} {fp[:10]}, перевірено {tot}, пропусків {miss}")
print([fix(w) for w in ['гудзик','ганок','гедзь','грунт','гава','галицький','агрус','гвалт','гуля','гречка','голова','мегабайт']])
