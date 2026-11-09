"""Калібрування: короткі слова за списком + контекст речення. Логіка дзеркалиться в AutoDetector.cs."""
import random, build_model as bm
uk=open('uk.txt').read().split('\n'); en=open('en.txt').read().split('\n')
L={'uk':bm.Lang(uk,bm.UA_ALPHA),'en':bm.Lang(en,bm.EN_ALPHA)}

UA_SKIP={'р','м','п','х','т','сша','рф','ст','млн','грн','сбу','єс','шо','го','дня','сил','тис','суд'}
EN_SKIP={'s','d','t','m','mr',"i'm"}
UA_SINGLE={'в','і','у','з','а','я','й','є','ж','б','о'}
def shortlist(words, skip, singles, n=500):
    out=set()
    for w in words[:n]:
        if len(w)<=3 and w not in skip and (len(w)>1 or w in singles): out.add(w)
    return out
SHORT={'uk':shortlist(uk,UA_SKIP,UA_SINGLE),'en':shortlist(en,EN_SKIP,{'a','i'})}

SHORT_CUR_RANK=3000
CTX_MIN=2          # скільки слів контексту потрібно
CTX_CUR_RANK=1500  # слово поточної мови, частіше за це, не чіпаємо навіть за контекстом

def decide2(text, cur, alt_text, alt, ctx=None):
    """ctx: (ua_count, en_count) попередніх слів речення."""
    craw, araw = bm.core(text, alt_text)
    c, a = craw.lower(), araw.lower()
    if len(c) < 2 or len(a) < 2 or any(ch.isdigit() for ch in text) or '@' in text: return False
    favors_alt = favors_cur = False
    if ctx:
        n_alt = ctx[0] if alt=='uk' else ctx[1]; n_cur = ctx[1] if alt=='uk' else ctx[0]
        favors_alt = n_alt >= CTX_MIN and n_alt > n_cur
        favors_cur = n_cur >= CTX_MIN and n_cur > n_alt
    cr, ar = L[cur].rank.get(c), L[alt].rank.get(a)
    if len(c) <= 3:
        if c in SHORT[cur]: return False
        if a in SHORT[alt] and (cr is None or cr > SHORT_CUR_RANK): return True
    if cr is not None:
        if len(c) <= 3 and ar is not None and L[alt].known(a) and ar * bm.RATIO < cr: return True
        # відоме слово поточної мови: міняємо лише якщо речення іншою мовою, а слово не надто часте
        return favors_alt and ar is not None and L[alt].known(a) and cr > CTX_CUR_RANK and len(c) >= 3
    if L[alt].known(a): return True
    if len(c) < 4 or craw.isupper(): return False
    if favors_cur: return False      # у реченні поточною мовою «шум» не перекладаємо за n-грамами
    sc, sa = L[cur].score(c), L[alt].score(a)
    return sc < bm.ABS_BAD and sa - sc > bm.MARGIN

if __name__=='__main__':
    random.seed(5)
    def zipf(ws,n): return random.choices(ws,[1/(i+1) for i in range(len(ws))],k=n)
    zu=[w for w in zipf(uk,30000) if len(w)>=2]; ze=[w for w in zipf(en,30000) if len(w)>=2]
    def rate(f, pairs): return sum(f(*p) for p in pairs)/len(pairs)
    old=lambda t,c,a,al,ctx=None: bm.decide(t,c,a,al,L)
    for name,f in (('ДО',old),('ПІСЛЯ',decide2)):
        print(f'== {name}')
        print(' без контексту: UA в EN виправлено', round(rate(f,[(bm.type_in(w,'en'),'en',w,'uk',None) for w in zu]),4),
              ' EN в UA', round(rate(f,[(bm.type_in(w,'uk'),'uk',w,'en',None) for w in ze]),4))
        print(' без контексту: хибні UA', round(rate(f,[(w,'uk',bm.type_in(w,'en'),'en',None) for w in zu]),5),
              ' хибні EN', round(rate(f,[(w,'en',bm.type_in(w,'uk'),'uk',None) for w in ze]),5))
        print(' укр. речення, слово в EN-розкладці: виправлено', round(rate(f,[(bm.type_in(w,'en'),'en',w,'uk',(3,0)) for w in zu]),4))
        print(' англ. речення, слово в UA-розкладці: виправлено', round(rate(f,[(bm.type_in(w,'uk'),'uk',w,'en',(0,3)) for w in ze]),4))
        print(' ХИБНІ: англ. слово серед укр. речення', round(rate(f,[(w,'en',bm.type_in(w,'uk'),'uk',(3,0)) for w in ze]),5),
              ' укр. слово серед англ.', round(rate(f,[(w,'uk',bm.type_in(w,'en'),'en',(0,3)) for w in zu]),5))
    fp=sorted({w for w in ze if decide2(w,'en',bm.type_in(w,'uk'),'uk',(3,0)) and not old(w,'en',bm.type_in(w,'uk'),'uk')})
    print('нові хибні EN серед укр. речення:', len(fp), fp[:40])
    fp2=sorted({w for w in zu+ze if (decide2(w,'uk',bm.type_in(w,'en'),'en') if w in L['uk'].rank else decide2(w,'en',bm.type_in(w,'uk'),'uk'))})
    print('хибні без контексту:', fp2[:30])
    for t,c,a,al,ctx in [('herb','en','руки','uk',(3,0)),('yt','en','не','uk',None),('ye','en','ну','uk',None),('db','en','ви','uk',None),('ok','en','щл','uk',None),('nbc','en','тис','uk',(3,0)),('Word','en','Цщкв','uk',(3,0)),('file','en','ашду','uk',(3,0)),('email','en','уьфшд','uk',(3,0)),('online','en','щтдшту','uk',(3,0))]:
        print(t,'->',a, decide2(t,c,a,al,ctx))
