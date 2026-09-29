# Блок-схемы алгоритмов (БСА)

Текстовые описания структур блок-схем для переноса в draw.io / Visio / Word:
прямоугольник — действие, ромб — условие, параллелограмм — ввод/вывод, овал — начало/конец.

Нумерация соответствует Documentation/Algorithms.md.

---

## №1. Постоянная функция — O(1)

```text
Start
 ↓
acc ← 0; i ← 0
 ↓
i < n?  ── нет ──→ Вывод acc → End
 ↓ да
acc ← acc + 7
 ↓
i ← i + 1
 ↓
(возврат к проверке условия)
```

---

## №2. Сумма элементов — O(n)

```text
Start
 ↓
Ввод: массив a длины m; проходов k
 ↓
sum ← 0; p ← 0
 ↓
p < k?  ── нет ──→ Вывод sum → End
 ↓ да
i ← 0
 ↓
i < m?  ── нет ──→ p ← p + 1 → (возврат к p < k)
 ↓ да
sum ← sum + a[i]
 ↓
i ← i + 1
 ↓
(возврат к i < m)
```

---

## №3. Произведение элементов — O(n)

```text
Start
 ↓
Ввод: массив a; модуль P
 ↓
prod ← 1; p ← 0
 ↓
p < k?  ── нет ──→ Вывод prod → End
 ↓ да
i ← 0
 ↓
i < m?  ── нет ──→ p ← p + 1 → (возврат)
 ↓ да
prod ← (prod · a[i]) mod P
 ↓
i ← i + 1
 ↓
(возврат к i < m)
```

---

## №4а. Многочлен наивно — O(n²)

```text
Start
 ↓
Ввод: коэффициенты a[0..n], x
 ↓
result ← 0; i ← 0
 ↓
i ≤ n?  ── нет ──→ Вывод result → End
 ↓ да
xk ← 1; j ← 1
 ↓
j ≤ i?  ── нет ──→ result ← result + a[i]·xk; i ← i + 1 → (возврат к i ≤ n)
 ↓ да
xk ← xk · x; j ← j + 1
 ↓
(возврат к j ≤ i)
```

---

## №4б. Метод Горнера — O(n)

```text
Start
 ↓
Ввод: коэффициенты a[0..n], x
 ↓
result ← a[n]; i ← n−1
 ↓
i ≥ 0?  ── нет ──→ Вывод result → End
 ↓ да
result ← result · x + a[i]
 ↓
i ← i − 1
 ↓
(возврат к i ≥ 0)
```

---

## №5а. Итеративное возведение в степень — O(n)

```text
Start
 ↓
Ввод: a, n
 ↓
result ← 1; i ← 0
 ↓
i < n?  ── нет ──→ Вывод result → End
 ↓ да
result ← result · a mod m
 ↓
i ← i + 1
 ↓
(возврат к i < n)
```

---

## №5б. Рекурсивное возведение в степень — O(n)

```text
Start
 ↓
Ввод: a, k
 ↓
k = 0?  ── да ──→ возврат 1 → End
 ↓ нет
sub ← power(a, k − 1)      # рекурсивный вызов
 ↓
result ← a · sub mod m
 ↓
возврат result → End
```

---

## №5в. Бинарное возведение в степень — O(log n)

```text
Start
 ↓
Ввод: a, n
 ↓
result ← 1; b ← a mod m; e ← n
 ↓
e > 0?  ── нет ──→ Вывод result → End
 ↓ да
e нечётно (e & 1 = 1)?
 ├─ да → result ← result · b mod m
 ↓
b ← b · b mod m
 ↓
e ← e >> 1
 ↓
(возврат к e > 0)
```

---

## №6. Bubble Sort — O(n²)

```text
Start
 ↓
Ввод: массив a длины n
 ↓
end ← n − 1
 ↓
end > 0?  ── нет ──→ End
 ↓ да
swapped ← false; j ← 0
 ↓
j < end?  ── нет ──→ swapped?  ── нет ──→ End (массив отсортирован)
 ↓ да                     ↓ да
a[j] > a[j+1]?  ── нет ──→ j ← j + 1 → (возврат к j < end)
 ↓ да
обменять a[j], a[j+1]; swapped ← true; j ← j + 1
 ↓
(возврат к j < end)
после внутреннего цикла: end ← end − 1 → (возврат к end > 0)
```

---

## №7. Quick Sort — O(n log n)

```text
Start
 ↓
Ввод: массив a, границы lo, hi
 ↓
hi − lo + 1 < 16?  ── да ──→ InsertionSort(lo, hi) → End
 ↓ нет
mid ← (lo + hi) / 2
 ↓
a[mid] < a[lo]? → обменять
 ↓
a[hi] < a[lo]? → обменять
 ↓
a[hi] < a[mid]? → обменять      # медиана трёх
 ↓
pivot ← a[mid]; переместить pivot на позицию hi−1
 ↓
i ← lo; j ← hi − 1
 ↓
(цикл разделения:)
сканировать i вправо, пока a[i] < pivot
 ↓
сканировать j влево, пока a[j] > pivot
 ↓
i ≥ j?  ── да ──→ обменять a[i], a[hi−1]; p ← i
 ↓ нет
обменять a[i], a[j]; i++; j−− → (возврат к сканированию)
 ↓
QuickSort(a, lo, p−1) → QuickSort(a, p+1, hi) → End
```

---

## №8. TimSort — O(n log n)

```text
Start
 ↓
Ввод: массив a; n ← длина
 ↓
n < 2?  ── да ──→ End
 ↓ нет
minrun ← CalcMinRun(n)          # 32..63
 ↓
i ← 0
 ↓
i < n?  ── нет ──→ слить все серии стека → End
 ↓ да
runEnd ← FindRunEnd(a, i)       # естественная серия (убывающая → разворот)
 ↓
длина серии < minrun?  ── да ──→ InsertionSort(a, i, i+minrun−1); runEnd ← i+minrun−1
 ↓ нет
push (i, runEnd−i+1) в стек серий
 ↓
stackSize ← MergeCollapse()     # инварианты баланса: сливать соседние серии
 ↓                              # по правилам CPython
i ← runEnd + 1
 ↓
(возврат к i < n)
```

---

## №9. Классическое умножение матриц — O(n³)

```text
Start
 ↓
Ввод: матрицы A, B (n×n); обнуление C
 ↓
i ← 0
 ↓
i < n?  ── нет ──→ Вывод C → End
 ↓ да
k ← 0
 ↓
k < n?  ── нет ──→ i ← i + 1 → (возврат к i < n)
 ↓ да
aik ← A[i,k]; j ← 0
 ↓
j < n?  ── нет ──→ k ← k + 1 → (возврат к k < n)
 ↓ да
C[i,j] ← C[i,j] + aik · B[k,j]
 ↓
j ← j + 1
 ↓
(возврат к j < n)
```

---

## №10. Индивидуальный: быстрое возведение в степень — O(log n)

Совпадает с блок-схемой №5в (бинарное возведение в степень).

---

## №11. Индивидуальный: алгоритм Кадане — O(n)

```text
Start
 ↓
Ввод: массив a длины m
 ↓
maxEnding ← 0; maxSoFar ← −∞; i ← 0
 ↓
i < m?  ── нет ──→ Вывод maxSoFar → End
 ↓ да
cand ← maxEnding + a[i]
 ↓
cand > a[i]?  ── да ──→ maxEnding ← cand ─┐
 ↓ нет                                    ├→ maxEnding
maxEnding ← a[i] ─────────────────────────┘
 ↓
maxEnding > maxSoFar?  ── да ──→ maxSoFar ← maxEnding
 ↓ нет
i ← i + 1
 ↓
(возврат к i < m)
```

---

## №12. Индивидуальный: реверс массива — O(n)

```text
Start
 ↓
Ввод: массив a длины n
 ↓
i ← 0; j ← n − 1
 ↓
i < j?  ── нет ──→ End
 ↓ да
обменять a[i], a[j]
 ↓
i ← i + 1; j ← j − 1
 ↓
(возврат к i < j)
```
