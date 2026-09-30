# Migration and Compatibility Notes

## Decision: no compatibility adapter for the `Smart*` attributes

Most observable legacy behavior is buggy (see ARCHAEOLOGY §2.2). An adapter would face a
choice:

- reproduce those bugs, which is worthless; or
- silently change behavior under the old names, which is dangerous.

The legacy code base is also 400 lines with one entry point, so hand-migrating a model is fast. The legacy sources
are kept at the [`legacy-2019` tag](https://github.com/Murat7Ay/AttributeBasedValidation/tree/legacy-2019).

## Attribute mapping

| Legacy | New (`Conform.Annotations` + BCL DataAnnotations) |
|---|---|
| `[SmartString]` | `[Required]` |
| `[SmartString(notEmptyOrNullOrWhiteSpace: false, ...)]` | omit `[Required]`; the other rules now *do* run on present values |
| `minLength: a, maxLength: b` | `[StringLength(b, MinimumLength = a)]` |
| `regexPattern: p` | `[RegularExpression(p)]` (full match), or `Rules.Matches(p)` in code (unanchored, like legacy) |
| `onlyDigit: true, changeProperty: true` | `[DigitsOnly]` |
| `onlyDigit: true` (changeProperty false) | `[DigitsOnly(Lens = true)]` |
| `subStringIndex: n` | `[KeepLast(n)]`; a value that is too short is a `NormalizationFailed` finding |
| both of the above | `[DigitsOnly(Order = 1), KeepLast(10, Order = 2)]`; distinct `Order` is required |
| `ignoreWhenPrompted: true` | `[SkipIn("your-scenario")]` + `Evaluate(x, new() { Scenarios = ["your-scenario"] })` |
| `[SmartDateTime(notNullOrDefault: true, ...)]` | `[Required]` (nullable) and/or `[NotDefault]` |
| `greaterOrEqualThanTomorrow: true` | `[NotBeforeToday(1)]` |
| `changeIfDefault: true, year, month, day` | `[DefaultIfMissing("yyyy-MM-dd")]` |
| `[SmartChildClass(isCollection, min, notNull)]` | `[Descend]` + `[Required]` (if notNull) + `[MinLength(min)]` |
| `ValidationHelper.CheckModel(obj, ignore)` | `new ConformanceEngine(new ContractRegistry().UseAnnotations()).Evaluate(obj, options)` |
| `result.IsValid` / `result.Error` | `report.IsConformant` / `report.Findings` (all of them, with paths) |
| (implicit mutation) | `report.Apply()` |

## Legacy behavior deliberately not preserved

| Legacy behavior | Classification | New behavior |
|---|---|---|
| Mutation during validation, before the failing checks | bug | evaluation is pure; `Apply()` is explicit and conflict-checked |
| `ChangeIfDefault` overwrites any value | bug | only absent/default values are replaced |
| `GreaterOrEqualThanTomorrow` ignored unless `NotNullOrDefault` | bug | rules run whenever a value is present |
| Length/regex ignored unless `NotEmptyOrNullOrWhiteSpace` | bug | rules run whenever a value is present |
| Optional child (`notNull:false`) never traversed | bug | present children are always traversed along declared edges |
| Scenario flag lost in recursion | bug | scenarios apply to the whole graph |
| Regex failure reported as "exceed length" | bug | `pattern` / `regular-expression` code |
| `"AppointmentDateinvalid date"` (missing space), hard-coded "invalid gsm number" | bug | messages come from rules |
| `HashSet<T>` / lazy enumerables skipped | accident | any non-string `IEnumerable` |
| Fields with attributes ignored | accident | public fields are scanned |
| `SmartString` on non-string ignored silently | accident | misapplied rules fail at definition time |
| Stack overflow on cycles | bug | identity-based traversal |
| First error only, no path | limitation | all findings, with paths |
| Unsatisfiable `Child.NestedChild` model | bug in sample | not reproduced; the migrated sample uses an optional edge |
| `Regex.Match` unanchored | intentional | kept in `Rules.Matches`; BCL `[RegularExpression]` is a full match |
| Null root → error, not exception | intentional | kept: `required` finding at the root |
| Whitespace-only strings count as missing | intentional | kept, and extended to all presence checks |

## Known semantic differences from plain DataAnnotations

- `[Required(AllowEmptyStrings = true)]` is not honored. Whitespace strings are absent.
- An empty or whitespace string is *absent*, so non-`Required` attributes do not run on it. For example,
  `[StringLength(10, MinimumLength = 5)]` passes `""`. Plain `Validator` would fail it.
- Attributes see the *canonical* value (after normalizers and lenses), not the raw value.
- `IValidatableObject` is not adapted. Use a class-level `ValidationAttribute`, a `RuleAttribute`, or a code
  contract.
