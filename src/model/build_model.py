"""Готує словники для UaSwitch і калібрує логіку автоматичного виправлення.

Дані: wordfreq (Robyn Speer та ін., CC BY-SA 4.0). Вихід: uk.txt, en.txt (слова в порядку частоти).
Логіка оцінювання тут повторює C#-реалізацію (AutoDetector.cs) — пороги калібруються на цьому скрипті.
"""
import math, re, sys, random
import wordfreq

UK_N, EN_N = 80000, 60000

def load():
    uk = []
    for w in wordfreq.top_n_list('uk', 250000):
        w = w.replace('’', "'").replace('ʼ', "'")
        if not re.fullmatch(r"[а-щьюяіїєґ']+", w) or w.startswith("'") or w.endswith("'"):
            continue
        fu, fr = wordfreq.word_frequency(w, 'uk'), wordfreq.word_frequency(w, 'ru')
        if fr > 4 * fu:          # відсіюємо російські слова з українського корпусу
            continue
        if w not in uk:
            uk.append(w)
        if len(uk) >= UK_N:
            break
    en = []
    for w in wordfreq.top_n_list('en', 200000):
        if not re.fullmatch(r"[a-z]+('[a-z]+)?", w):
            continue
        en.append(w)
        if len(en) >= EN_N:
            break
    return uk, en

# --- Розкладки (для симуляції; у програмі таблиці беруться з Windows) ---
EN_KEYS = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./"
UA_KEYS = "'йцукенгшщзхїфівапролджєячсмитьбю."
EN2UA = {e: u for e, u in zip(EN_KEYS, UA_KEYS)}
UA2EN = {u: e for e, u in zip(EN_KEYS, UA_KEYS)}
EN2UA['\\'] = 'ґ'; UA2EN['ґ'] = '\\'

def type_in(word, target):  # як виглядатиме слово, набране не в тій розкладці
    m = UA2EN if target == 'en' else EN2UA
    return ''.join(m.get(c, c) for c in word)

# --- Модель ---
class Lang:
    def __init__(self, words, alphabet):
        self.rank = {w: i for i, w in enumerate(words)}
        self.alpha = set(alphabet)
        tri, bi = {}, {}
        for i, w in enumerate(words):
            wt = 1.0 / math.sqrt(i + 10)
            s = '^^' + w + '$'
            for j in range(2, len(s)):
                k = s[j-2:j+1]
                tri[k] = tri.get(k, 0) + wt
                bi[k[:2]] = bi.get(k[:2], 0) + wt
        self.tri, self.bi = tri, bi
        self.V = len(self.alpha) + 1

    def known(self, w):
        r = self.rank.get(w)
        if r is None: return False
        n = len(w)
        return r < (1500 if n <= 2 else 8000 if n == 3 else 10**9)

    def score(self, w):  # середній log P на символ; символи поза абеткою — штраф
        s = '^^' + w + '$'
        tot = 0.0
        for j in range(2, len(s)):
            c = s[j]
            if c != '$' and c not in self.alpha:
                tot += -12.0
                continue
            k = s[j-2:j+1]
            tot += math.log((self.tri.get(k, 0) + 0.02) / (self.bi.get(k[:2], 0) + 0.02 * self.V))
        return tot / (len(s) - 2)

UA_ALPHA = "абвгґдеєжзиіїйклмнопрстуфхцчшщьюя'"
EN_ALPHA = "abcdefghijklmnopqrstuvwxyz'"

PUNCT = set('.,!?;:")»')

def core(text, alt_text):
    """Відрізає хвостові розділові знаки — ті, що є знаками в ОБОХ прочитаннях (інакше це літера)."""
    i = len(text)
    while i > 0 and alt_text[i-1] in PUNCT and text[i-1] in PUNCT | set("/?&^$#*"):
        i -= 1
    return text[:i], alt_text[:i]

MARGIN, ABS_BAD, RATIO = 2.0, -4.6, 30

def decide(text, cur, alt_text, alt, L):
    """Чи треба змінити розкладку: text — як є в поточній розкладці cur, alt_text — в іншій."""
    craw, araw = core(text, alt_text)
    c, a = craw.lower(), araw.lower()
    if len(c) < 2 or len(a) < 2 or any(ch.isdigit() for ch in text) or '@' in text:
        return False
    cr, ar = L[cur].rank.get(c), L[alt].rank.get(a)
    if cr is not None:
        # Відоме слово не чіпаємо; виняток — коротке рідкісне слово, коли в іншій мові
        # воно дуже часте («yt» → «не», «vb» → «ми»)
        if len(c) <= 3 and ar is not None and L[alt].known(a) and ar * RATIO < cr:
            return True
        return False
    if L[alt].known(a):
        return True
    if len(c) < 4 or craw.isupper():
        return False
    sc, sa = L[cur].score(c), L[alt].score(a)
    return sc < ABS_BAD and sa - sc > MARGIN

if __name__ == '__main__':
    uk, en = load()
    open('uk.txt', 'w').write('\n'.join(uk))
    open('en.txt', 'w').write('\n'.join(en))
    L = {'uk': Lang(uk, UA_ALPHA), 'en': Lang(en, EN_ALPHA)}
    print('words', len(uk), len(en))

    # Тестові вибірки: слова з «хвоста» словників (рідше трапляються, складніші) + частотні
    random.seed(1)
    def sample(ws, n): return random.sample(ws[:20000], n // 2) + random.sample(ws[20000:], n // 2)
    tu, te = sample(uk, 4000), sample(en, 4000)
    def rate(pairs):
        return sum(decide(t, c, a, al, L) for t, c, a, al in pairs) / len(pairs)
    print('UA набрано в EN  -> виправлено:', rate([(type_in(w, 'en'), 'en', w, 'uk') for w in tu]))
    print('EN набрано в UA  -> виправлено:', rate([(type_in(w, 'uk'), 'uk', w, 'en') for w in te]))
    print('UA правильне     -> помилково :', rate([(w, 'uk', type_in(w, 'en'), 'en') for w in tu]))
    print('EN правильне     -> помилково :', rate([(w, 'en', type_in(w, 'uk'), 'uk') for w in te]))
    # Реалістичний текст: слова з частотою як у живій мові (Zipf)
    def zipf(ws, n):
        weights = [1 / (i + 1) for i in range(len(ws))]
        return random.choices(ws, weights, k=n)
    zu, ze = zipf(uk, 20000), zipf(en, 20000)
    print('ТЕКСТ: UA в EN виправлено   :', rate([(type_in(w, 'en'), 'en', w, 'uk') for w in zu]))
    print('ТЕКСТ: EN в UA виправлено   :', rate([(type_in(w, 'uk'), 'uk', w, 'en') for w in ze]))
    print('ТЕКСТ: UA правильне хибно   :', rate([(w, 'uk', type_in(w, 'en'), 'en') for w in zu]))
    print('ТЕКСТ: EN правильне хибно   :', rate([(w, 'en', type_in(w, 'uk'), 'uk') for w in ze]))
    fpz = sorted({w for w in zu if decide(w, 'uk', type_in(w, 'en'), 'en', L)} | {w for w in ze if decide(w, 'en', type_in(w, 'uk'), 'uk', L)})
    print('ТЕКСТ: хибні слова:', fpz[:30])
    mz = {}
    multi = [w for w in zu if len(w) >= 2]
    print('ТЕКСТ (без однобуквених): UA в EN виправлено:', rate([(type_in(w, 'en'), 'en', w, 'uk') for w in multi]))
    for w in zu:
        if len(w) >= 2 and not decide(type_in(w, 'en'), 'en', w, 'uk', L): mz[w] = mz.get(w, 0) + 1
    print('ТЕКСТ: найчастіші пропуски UA:', sorted(mz.items(), key=lambda x: -x[1])[:25])
    extra = ['PFMEA', 'AQAP', 'NCR', 'iso', 'Stratoris', 'Antonov', 'Uzhhorod', 'kyiv', 'ok', 'lol', 'github',
             'excel', 'outlook', 'teams', 'qms', 'capa', 'gqar', 'covid', 'wifi', 'iphone', 'peugeot', 'zakarpattia']
    fp = [w for w in extra if decide(w, 'en', type_in(w.lower(), 'uk'), 'uk', L)]
    print('Спец. слова EN, помилково виправлені:', fp)
    if len(sys.argv) > 1:
        miss_ua = [w for w in tu if not decide(type_in(w, 'en'), 'en', w, 'uk', L)][:40]
        fp_ua = [w for w in tu if decide(w, 'uk', type_in(w, 'en'), 'en', L)][:40]
        fp_en = [w for w in te if decide(w, 'en', type_in(w, 'uk'), 'uk', L)][:40]
        print('пропущено UA:', miss_ua); print('FP UA:', fp_ua); print('FP EN:', fp_en)
