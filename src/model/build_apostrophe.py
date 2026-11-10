"""Будує src/apo.txt.gz — ключі для автоапострофа («мясо» → «м'ясо»).

Джерело: український словник hunspell проєкту spell-uk з репозиторію LibreOffice
(https://github.com/LibreOffice/dictionaries/tree/master/uk_UA), ліцензія MPL 1.1.
Похідний файл apo.txt.gz поширюється за тією ж ліцензією MPL 1.1.

Кроки:
  1. Завантажити uk_UA.dic і uk_UA.aff.
  2. unmunch uk_UA.dic uk_UA.aff > forms.txt     (пакет hunspell-tools)
  3. python3 build_apostrophe.py forms.txt ../apo.txt.gz

Ключ «м'я» означає: слово, що починається з «мя», пишеться з апострофом після «м».
Ключ найкоротший з тих, що не збігаються з початком жодного правильного слова без апострофа,
тож правильні слова («бюро», «свято», «буряк») ніколи не змінюються. «…$» — ключ лише для цілого слова.
"""
import bisect
import gzip
import re
import sys

src, dst = sys.argv[1], sys.argv[2]
ok = re.compile(r"^[а-щьюяєіїґ'\-]+$")
words = set()
for line in open(src, encoding="utf8"):
    w = line.strip().split("/")[0].lower()
    if ok.match(w):
        words.add(w)

# Правильні слова без апострофа, яких бракує в словнику (інакше отримали б зайвий апостроф)
for w in ["дарять", "дарячи", "мінюсь", "мінюся", "перемирюсь", "перемирюся", "перемиряться",
          "стовбурюсь", "стовбурюся", "стовбуряться", "обюрокрачений", "обюрокрачена",
          "обюрокрачене", "обюрокрачені", "обюрокрачення", "обюрокрачування"]:
    words.add(w)

apo = {w for w in words if "'" in w}
plain = sorted(w for w in words if "'" not in w)


def has_prefix(p):
    i = bisect.bisect_left(plain, p)
    return i < len(plain) and plain[i].startswith(p)


keys = set()
for c in apo:
    if c.count("'") != 1:
        continue
    i = c.index("'")
    left, right = c[:i], c[i + 1:]
    if not left or not right or c.replace("'", "") in words:
        continue  # обидва варіанти правильні — не чіпаємо
    for k in range(1, len(right) + 1):
        if not has_prefix(left + right[:k]):
            keys.add(left + "'" + right[:k])
            break
    else:
        keys.add(c + "$")

open(dst, "wb").write(gzip.compress("\n".join(sorted(keys)).encode("utf8"), 9))
print(f"словоформ {len(words)}, з апострофом {len(apo)}, ключів {len(keys)}")
