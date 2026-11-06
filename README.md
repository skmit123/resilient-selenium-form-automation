# Resilient Selenium Form Automation (C#)

A C# + Selenium program that fills three fields on the CloudQA practice form (First Name, Gender, State) **without relying on fixed XPath/CSS selectors**, so it keeps working when the page is modified.

Target page: http://app.cloudqa.io/home/AutomationPracticeForm (URL is configurable).

## How it finds fields

Every candidate element is scored on several independent signals, and the best score wins:

| Signal | Weight | Examples |
|---|---|---|
| Label text | 10 | `<label for>`, wrapping label, `aria-labelledby`, text next to a radio |
| Placeholder / aria-label / title | 8 | `placeholder="First name"` |
| Nearby text | 6 | previous sibling text, `<legend>`, `role=radiogroup` |
| Attributes | 5 | `id`, `name`, `class`, `value` tokens |

Extra rules:

- Negative keywords (for example `last`, `sur`, `middle` for First Name) subtract from the score, so look-alike fields are not picked.
- Gender matches the radio's **own** label or value exactly, so `Male` never selects `Female`.
- State is a `<select>`; the option is matched by visible text or value, and the program verifies the selection.
- Candidates are collected from the main document, open shadow roots and iframes. Hidden or disabled elements are ignored.
- Every step reads the value back and prints PASS or FAIL.

## Prerequisites

- .NET 8 SDK
- Google Chrome (Selenium Manager downloads the matching driver automatically)

## Run

```bash
git clone https://github.com/skmit123/resilient-selenium-form-automation.git
cd resilient-selenium-form-automation
dotnet restore
dotnet run -- --url "http://app.cloudqa.io/home/AutomationPracticeForm" --first "Aarav" --gender "Male" --state "India"
```

### Options

| Option | Default | Description |
|---|---|---|
| `--url` | env `FORM_URL`, else the CloudQA URL | Page to test |
| `--first` | `Aarav` | Text typed into First Name |
| `--gender` | `Male` | `Male`, `Female` or `Transgender` |
| `--state` | `India` | Option text in the State dropdown (this page lists countries) |
| `--headless` | off | Run Chrome without a window |

The URL can also be set with an environment variable:

```bash
FORM_URL="http://localhost:8000/page.html" dotnet run
```

Expected output:

```
[PASS] First Name
[PASS] Gender
[PASS] State
ALL CHECKS PASSED
```

The exit code is `0` when all checks pass and `1` otherwise.

## Testing against modified pages

1. Save the page HTML as `page.html` in a folder, for example `~/mutations`.
2. Serve it: `cd ~/mutations && python3 -m http.server 8000`
3. Run: `dotnet run -- --url "http://localhost:8000/page.html"`
4. Edit `page.html`, save, and rerun after each change.

| Mutation | Result |
|---|---|
| Baseline (unmodified copy) | PASS |
| Renamed `id` and `name` of First Name | PASS |
| Removed `for=` from labels | to run |
| Reordered fields | to run |
| Added a Middle Name input | to run |
| Changed gender markup / values | to run |

## Limitations

- Custom (non-`<select>`) dropdowns are not handled.
- Closed shadow roots cannot be searched.
- Keyword lists (First Name, State synonyms) are in `Program.cs`; extend them for other languages or wording.
