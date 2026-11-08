import bisect, math, random, re, wordfreq
import build_model as bm
uk=open('uk.txt').read().split('\n'); en=open('en.txt').read().split('\n')
class P:
    def __init__(s, words):
        pairs=sorted((w, 1/math.sqrt(i+10)) for i,w in enumerate(words))
        s.w=[p[0] for p in pairs]; s.c=[0.0]
        for p in pairs: s.c.append(s.c[-1]+p[1])
    def weight(s, pre):
        lo=bisect.bisect_left(s.w, pre); hi=bisect.bisect_left(s.w, pre+'￿')
        return s.c[hi]-s.c[lo]
PU, PE = P(uk), P(en)
PR={'uk':PU,'en':PE}; AL={'uk':set(bm.UA_ALPHA),'en':set(bm.EN_ALPHA)}
MINLEN, T = 4, 0.02
def early(cur_text, cur, alt_text, alt):
    """повертає довжину префікса, на якій спрацює (або None)"""
    for n in range(MINLEN, len(cur_text)+1):
        c, a = cur_text[:n], alt_text[:n]
        if any(ch.isdigit() for ch in c): return None
        if c.isupper() or any(ch.isupper() for ch in c[1:]): return None
        cl, al = c.lower(), a.lower()
        if not all(ch in AL[alt] for ch in al): continue   # у іншій мові поки не схоже на слово
        if PR[cur].weight(cl) == 0 and PR[alt].weight(al) > T:
            return n
    return None
random.seed(3)
tu=random.sample(uk,3000); te=random.sample(en,3000)
def stats(pairs, label):
    hits=[early(*p) for p in pairs]
    k=sum(h is not None for h in hits)
    print(f"{label}: спрацювало {k/len(pairs):.3f}", (f"середня позиція {sum(h for h in hits if h)/max(k,1):.1f}" if k else ""))
    return hits
stats([(bm.type_in(w,'en'),'en',w,'uk') for w in tu if len(w)>=4], 'UA набрано в EN (4+)')
stats([(bm.type_in(w,'uk'),'uk',w,'en') for w in te if len(w)>=4], 'EN набрано в UA (4+)')
fp1=[w for w in tu if early(w,'uk',bm.type_in(w,'en'),'en')]
fp2=[w for w in te if early(w,'en',bm.type_in(w,'uk'),'uk')]
print('FP словникові UA', len(fp1), fp1[:10], ' EN', len(fp2), fp2[:10])
# правильні слова поза словником: хвіст wordfreq
ukt=[w.replace('’',"'") for w in wordfreq.top_n_list('uk',250000)[150000:] if re.fullmatch(r"[а-щьюяіїєґ']{4,}",w.replace('’',"'"))]
ent=[w for w in wordfreq.top_n_list('en',250000)[120000:] if re.fullmatch(r"[a-z]{4,}",w)]
ukt=random.sample(ukt,4000); ent=random.sample(ent,4000)
f1=[w for w in ukt if early(w,'uk',bm.type_in(w,'en'),'en')]
f2=[w for w in ent if early(w,'en',bm.type_in(w,'uk'),'uk')]
print(f'FP рідкісні UA {len(f1)/len(ukt):.4f}', f1[:15]); print(f'FP рідкісні EN {len(f2)/len(ent):.4f}', f2[:15])
tech=['stratoris','uzhhorod','antonov','prepreg','autoclave','pfmea','gqar','qms','aqap','ncr','iatf','hexcel','toray','solvay','zakarpattia','mukachevo','kyivstar','sharepoint','onedrive','confluence','jira','powerbi','solidworks','catia','teamcenter','kanban','kaizen','poka','yoke','gemba']
print('tech EN FP', [w for w in tech if early(w,'en',bm.type_in(w,'uk'),'uk')])
techu=['препрег','автоклав','оснастка','стратоліт','склопластик','вуглепластик','ужгород','антонов','кондуктор','стапель','лонжерон','нервюра','стрингер','обшивка','кесон','закарпаття','мукачево','ґрунтовка','шпаклівка','ламінування']
print('tech UA FP', [w for w in techu if early(w,'uk',bm.type_in(w,'en'),'en')])
