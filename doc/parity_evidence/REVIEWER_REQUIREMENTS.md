# Reviewer Requirements For Blind Human-Eval

Эти требования задают минимальную reviewer-diversity discipline для authoritative blind human-eval.

## Minimums

1. Минимум `4` уникальных reviewer-а на весь корпус.
2. Ни один reviewer не должен занимать больше `45%` всех оценок.
3. По каждой `task family` должно быть минимум `2` reviewer-а.

## Preferred Targets

1. Предпочтительная цель: `4` reviewer-а на каждую `task family`.
2. По отдельной `task family` max reviewer share желательно держать не выше `60%`.
3. Корпус не должен быть effectively owned одним reviewer-ом даже при формальном прохождении global minimum.

## Interpretation

- `PASS`: reviewer coverage и spread достаточны для authoritative blind-eval chain.
- `WARN`: формат корпуса пригоден, но reviewer diversity узкий и ограничивает доверие.
- `FAIL`: blind-eval нельзя считать authoritative независимо от средних score.
