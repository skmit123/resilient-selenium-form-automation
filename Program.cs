using System.Text.RegularExpressions;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

var cfg = Config.Parse(args);
var opts = new ChromeOptions();
if (cfg.Headless) opts.AddArgument("--headless=new");
opts.AddArgument("--window-size=1400,1000");

using IWebDriver driver = new ChromeDriver(opts);
bool ok = true;

try
{
    driver.Navigate().GoToUrl(cfg.Url);
    new WebDriverWait(driver, TimeSpan.FromSeconds(15))
        .Until(d => d.FindElements(By.CssSelector("input,select")).Any());

    var finder = new SmartFinder(driver);

    ok &= Step("First Name", () =>
    {
        var el = finder.FindTextInput(
            new[] { "first name", "firstname", "first_name", "fname", "given name", "forename" },
            new[] { "last", "sur", "middle", "family", "full", "user" });
        el.Scroll(driver); el.Clear(); el.SendKeys(cfg.FirstName);
        return el.GetAttribute("value") == cfg.FirstName;
    });

    ok &= Step("Gender", () =>
    {
        var el = finder.FindRadio(cfg.Gender, new[] { "gender", "sex" });
        el.Scroll(driver);
        if (!el.Selected)
        {
            try { el.Click(); } catch { driver.JS("arguments[0].click()", el); }
        }
        return el.Selected;
    });

    ok &= Step("State", () =>
    {
        var sel = finder.FindSelect(new[] { "state", "province", "region" },
                                    new[] { "country", "city", "district", "zip", "postal" });
        sel.Scroll(driver);
        var s = new SelectElement(sel);
        var want = Util.Norm(cfg.State);
        var opt = s.Options.FirstOrDefault(o => Util.Norm(o.Text) == want || Util.Norm(o.GetAttribute("value")) == want)
               ?? s.Options.FirstOrDefault(o => Util.Norm(o.Text).Contains(want))
               ?? throw new Exception($"Option '{cfg.State}' not found");
        s.SelectByText(opt.Text);
        return Util.Norm(s.SelectedOption.Text) == Util.Norm(opt.Text);
    });
}
catch (Exception e) { Console.WriteLine("FATAL: " + e.Message); ok = false; }

Console.WriteLine(ok ? "ALL CHECKS PASSED" : "SOME CHECKS FAILED");
return ok ? 0 : 1;

static bool Step(string name, Func<bool> f)
{
    try { bool r = f(); Console.WriteLine($"[{(r ? "PASS" : "FAIL")}] {name}"); return r; }
    catch (Exception e) { Console.WriteLine($"[FAIL] {name}: {e.Message}"); return false; }
}

record Config(string Url, string FirstName, string Gender, string State, bool Headless)
{
    public static Config Parse(string[] a)
    {
        string Get(string k, string d) { int i = Array.IndexOf(a, "--" + k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : d; }
        var url = Get("url", Environment.GetEnvironmentVariable("FORM_URL") ?? "http://app.cloudqa.io/home/AutomationPracticeForm");
        return new(url, Get("first", "Aarav"), Get("gender", "Male"), Get("state", "India"), a.Contains("--headless"));
    }
}

static class Util
{
    public static string Norm(string? s) =>
        Regex.Replace((s ?? "").ToLowerInvariant().Replace("_", " ").Replace("-", " "), @"[^a-z0-9 ]|\s+", " ").Trim();

    public static void JS(this IWebDriver d, string script, params object[] a) =>
        ((IJavaScriptExecutor)d).ExecuteScript(script, a);

    public static void Scroll(this IWebElement e, IWebDriver d) =>
        d.JS("arguments[0].scrollIntoView({block:'center'})", e);
}

class SmartFinder
{
    readonly IWebDriver d;
    public SmartFinder(IWebDriver d) => this.d = d;

    // Light DOM first (document order), then open shadow roots, recursively.
    const string CollectJs = @"
const sel=arguments[0], light=[], deep=[];
document.querySelectorAll(sel).forEach(e=>light.push(e));
function walk(root){
  root.querySelectorAll('*').forEach(h=>{
    if(h.shadowRoot){ h.shadowRoot.querySelectorAll(sel).forEach(e=>deep.push(e)); walk(h.shadowRoot); }
  });
}
walk(document);
return light.concat(deep);";

    // Gathers every human-readable hint for an element.
    const string HintsJs = @"
const e=arguments[0], out={label:[],attr:[],ident:[],near:[]};
const t=n=>(n&&n.textContent||'').trim();
const isInput=n=>n&&n.nodeType==1&&n.matches('input,select,textarea');
if(e.labels) for(const l of e.labels) out.label.push(t(l));
const c=e.closest('label'); if(c) out.label.push(t(c));
const lb=e.getAttribute('aria-labelledby');
if(lb) lb.split(/\s+/).forEach(i=>{const x=document.getElementById(i); if(x) out.label.push(t(x));});
['aria-label','placeholder','title','alt'].forEach(a=>{const v=e.getAttribute(a); if(v) out.attr.push(v);});
['id','name','class','data-testid','data-label','autocomplete','value'].forEach(a=>{const v=e.getAttribute(a); if(v) out.ident.push(v);});
if(e.type==='radio'||e.type==='checkbox'){
  let s=e.nextSibling;
  while(s && !isInput(s) && !t(s)) s=s.nextSibling;
  if(s && !isInput(s) && t(s)) out.label.push(t(s));
}
let n=e, hops=0;
while(n && hops<3 && out.near.length==0){
  let p=n.previousSibling, k=0;
  while(p && k<3){ const s=t(p); if(s && s.length<80 && !isInput(p)){out.near.push(s); break;} p=p.previousSibling; k++; }
  if(!out.near.length){ n=n.parentElement; hops++; }
}
const fs=e.closest('fieldset'); if(fs&&fs.querySelector('legend')) out.near.push(t(fs.querySelector('legend')));
const g=e.closest('[role=radiogroup],[role=group]'); if(g){const gl=g.getAttribute('aria-label'); if(gl) out.near.push(gl);}
return out;";

    record Hints(List<string> Label, List<string> Attr, List<string> Ident, List<string> Near);

    Hints GetHints(IWebElement e)
    {
        var r = (Dictionary<string, object>)((IJavaScriptExecutor)d).ExecuteScript(HintsJs, e);
        List<string> L(string k) => ((IEnumerable<object>)r[k]).Select(x => Util.Norm(x?.ToString())).ToList();
        return new(L("label"), L("attr"), L("ident"), L("near"));
    }

    static bool Has(IEnumerable<string> src, string kw)
    {
        var k = Util.Norm(kw);
        return src.Any(s => (" " + s + " ").Contains(" " + k + " ") || s.Replace(" ", "").Contains(k.Replace(" ", "")));
    }

    int Score(IWebElement e, string[] want, string[] avoid)
    {
        var h = GetHints(e); int s = 0;
        foreach (var k in want)
        {
            if (Has(h.Label, k)) s += 10;
            if (Has(h.Attr, k)) s += 8;
            if (Has(h.Near, k)) s += 6;
            if (Has(h.Ident, k)) s += 5;
        }
        foreach (var k in avoid)
            if (Has(h.Label, k) || Has(h.Attr, k) || Has(h.Near, k) || Has(h.Ident, k)) s -= 25;
        return s;
    }

    List<IWebElement> Collect(string css) =>
        ((IEnumerable<object>)((IJavaScriptExecutor)d).ExecuteScript(CollectJs, css)).Cast<IWebElement>().ToList();

    // Main document first, then every top-level iframe.
    T InAllFrames<T>(Func<T?> f) where T : class
    {
        d.SwitchTo().DefaultContent();
        var r = f(); if (r != null) return r;
        int n = d.FindElements(By.TagName("iframe")).Count;
        for (int i = 0; i < n; i++)
        {
            d.SwitchTo().DefaultContent();
            d.SwitchTo().Frame(i);
            r = f(); if (r != null) return r;
        }
        d.SwitchTo().DefaultContent();
        throw new NoSuchElementException("Element not found by any strategy");
    }

    static bool Usable(IWebElement e) { try { return e.Displayed && e.Enabled; } catch { return false; } }

    IWebElement? Best(IEnumerable<IWebElement> cands, string[] want, string[] avoid, int min = 5) =>
        cands.Where(Usable)
             .Select(e => (e, s: Score(e, want, avoid)))
             .Where(x => x.s >= min)
             .OrderByDescending(x => x.s)   // stable: earlier DOM position wins ties
             .Select(x => x.e)
             .FirstOrDefault();

    public IWebElement FindTextInput(string[] want, string[] avoid) => InAllFrames(() =>
        Best(Collect("input:not([type]),input[type=text],input[type=search]"), want, avoid));

    public IWebElement FindSelect(string[] want, string[] avoid) => InAllFrames(() =>
    {
        var sels = Collect("select");
        var hit = Best(sels, want, avoid);
        if (hit != null) return hit;
        // Fallback: a visible, non-penalised select that actually has options
        return sels.FirstOrDefault(s => Usable(s)
                                     && Score(s, Array.Empty<string>(), avoid) >= 0
                                     && new SelectElement(s).Options.Count > 1);
    });

    // Matches the radio whose OWN label/value equals the target, so "Male" never picks "Female".
    public IWebElement FindRadio(string target, string[] groupHints) => InAllFrames(() =>
    {
        var t = Util.Norm(target);
        IWebElement? best = null; int bestScore = 0;
        foreach (var r in Collect("input[type=radio]"))
        {
            if (!Usable(r)) continue;
            var h = GetHints(r); int s = 0;
            if (h.Label.Any(x => x == t)) s += 20;
            if (h.Attr.Any(x => x == t)) s += 15;
            if (h.Ident.Any(x => x == t)) s += 12;
            if (h.Label.Any(x => Regex.IsMatch(x, $@"\b{Regex.Escape(t)}\b"))) s += 3;
            if (s == 0) continue;
            if (groupHints.Any(g => Has(h.Near, g) || Has(h.Ident, g) || Has(h.Attr, g))) s += 10;
            if (s > bestScore) { best = r; bestScore = s; }
        }
        return best;
    });
}
