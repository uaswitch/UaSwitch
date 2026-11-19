# Будує src/ru.txt.gz — російські слова з ы/э/ъ/ё (wordfreq, 200 тис. найчастіших, CC BY-SA 4.0).
# Потрібен лише як захист: щоб звичайне російське слово («стороны») не перетворилося на українське («стороні»)
# у беті «Українське слово, набране російською розкладкою».
import gzip
from wordfreq import top_n_list
sig = sorted({w for w in top_n_list('ru', 200000) if any(c in w for c in 'ыэъё') and w.isalpha()})
open('../ru.txt.gz', 'wb').write(gzip.compress("\n".join(sig).encode(), 9))
print(len(sig))
