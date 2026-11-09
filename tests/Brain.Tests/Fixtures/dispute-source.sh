#!/usr/bin/env bash
# A source for brain's window, as sources.tsv names one: it prints its open questions as JSON. The dispute is modelled
# on a real split-complexity case: one method split into helpers across two files, the agent's reason naming the
# helpers, and gemma4 saying one of the reason's claims fails. A cyclomatic case marks each decision of a method where
# it sits, the || and && of one compound condition nested in its if, and its weights add up to its tally; a timeline
# shows the method before and the change that grew it. A last case's marks miss a decision and misquote another, so
# its tally does not add up, and the older step of its timeline holds a count that does not add up either. The repo is
# the first argument.
#
#   bash dispute-source.sh /path/to/repo
set -euo pipefail

repo="${1:?usage: dispute-source.sh <repo>}"

read -r -d '' questions <<'EOF' || true
{
  "source": "disputes",
  "questions": [
    {
      "id": "0000019",
      "repo": "@REPO@",
      "title": "DeterministicProblemClassifier.ClassifyAsync split into Classify and IsValueClass",
      "tags": ["split-complexity", "cyclomatic"],
      "created": "2026-11-01T08:12:00+01:00",
      "blocks": [
        {
          "type": "code",
          "caption": "The method the check flagged, before the fix",
          "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
          "startLine": 14,
          "code": "    public Task<ProblemClassification> ClassifyAsync(ProblemCase problem, CancellationToken cancellation)\n    {\n        cancellation.ThrowIfCancellationRequested();\n        if (problem.Check == \"split-complexity\" && problem.Helpers.Count > 0)\n        {\n            return Task.FromResult(ProblemClassification.Real(\"the split moved the paths into helpers\"));\n        }\n\n        if (problem.Check == \"flat-dispatch\" && problem.Arms.All(arm => arm.Statements == 1))\n        {\n            return Task.FromResult(ProblemClassification.FalsePositive(\"one statement per arm\"));\n        }\n\n        if (problem.Check == \"lcom4\")\n        {\n            foreach (var method in problem.Type.Methods)\n            {\n                if (!method.IsAccessor && method.Fields.Count > 0)\n                {\n                    return Task.FromResult(ProblemClassification.Real(\"a method reads state\"));\n                }\n            }\n\n            return Task.FromResult(ProblemClassification.FalsePositive(\"a value class\"));\n        }\n\n        return Task.FromResult(ProblemClassification.Unknown);\n    }",
          "highlights": [17, 22, 27, 29, 31]
        },
        {
          "type": "diff",
          "caption": "The fix",
          "files": [
            {
              "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
              "before": "namespace Navarone.Classifiers.Deterministic;\n\n/// <summary>Classifies the problems a rule decides without a model.</summary>\ninternal sealed class DeterministicProblemClassifier : IProblemClassifier\n{\n    public Task<ProblemClassification> ClassifyAsync(ProblemCase problem, CancellationToken cancellation)\n    {\n        cancellation.ThrowIfCancellationRequested();\n        if (problem.Check == \"split-complexity\" && problem.Helpers.Count > 0)\n        {\n            return Task.FromResult(ProblemClassification.Real(\"the split moved the paths into helpers\"));\n        }\n\n        if (problem.Check == \"flat-dispatch\" && problem.Arms.All(arm => arm.Statements == 1))\n        {\n            return Task.FromResult(ProblemClassification.FalsePositive(\"one statement per arm\"));\n        }\n\n        if (problem.Check == \"lcom4\")\n        {\n            foreach (var method in problem.Type.Methods)\n            {\n                if (!method.IsAccessor && method.Fields.Count > 0)\n                {\n                    return Task.FromResult(ProblemClassification.Real(\"a method reads state\"));\n                }\n            }\n\n            return Task.FromResult(ProblemClassification.FalsePositive(\"a value class\"));\n        }\n\n        return Task.FromResult(ProblemClassification.Unknown);\n    }\n}\n",
              "after": "namespace Navarone.Classifiers.Deterministic;\n\n/// <summary>Classifies the problems a rule decides without a model.</summary>\ninternal sealed class DeterministicProblemClassifier : IProblemClassifier\n{\n    public Task<ProblemClassification> ClassifyAsync(ProblemCase problem, CancellationToken cancellation)\n    {\n        cancellation.ThrowIfCancellationRequested();\n        return Task.FromResult(Classify(problem));\n    }\n\n    private static ProblemClassification Classify(ProblemCase problem)\n    {\n        if (problem.Check == \"split-complexity\" && problem.Helpers.Count > 0)\n        {\n            return ProblemClassification.Real(\"the split moved the paths into helpers\");\n        }\n\n        if (problem.Check == \"flat-dispatch\" && problem.Arms.All(arm => arm.Statements == 1))\n        {\n            return ProblemClassification.FalsePositive(\"one statement per arm\");\n        }\n\n        if (problem.Check == \"lcom4\")\n        {\n            return ValueClassRule.IsValueClass(problem.Type)\n                ? ProblemClassification.FalsePositive(\"a value class\")\n                : ProblemClassification.Real(\"a method reads state\");\n        }\n\n        return ProblemClassification.Unknown;\n    }\n}\n"
            },
            {
              "path": "src/Navarone.Classifiers/Deterministic/ValueClassRule.cs",
              "before": "",
              "after": "namespace Navarone.Classifiers.Deterministic;\n\n/// <summary>The value class rule lcom4 asks: no method but an accessor reads a field.</summary>\ninternal static class ValueClassRule\n{\n    public static bool IsValueClass(TypeFacts type)\n    {\n        foreach (var method in type.Methods)\n        {\n            if (!method.IsAccessor && method.Fields.Count > 0)\n            {\n                return false;\n            }\n        }\n\n        return true;\n    }\n}\n"
            }
          ]
        },
        {
          "type": "text",
          "caption": "Why the agent split it",
          "text": "`ClassifyAsync` only adapts the `IProblemClassifier` contract to a `Task`; `Classify` is one rule per check, and `IsValueClass` is the value class predicate the lcom4 rule asks, a separate question with its own loop over the class's methods."
        },
        {
          "type": "text",
          "caption": "Why gemma4 disagrees",
          "text": "The split moves the loop into `ValueClassRule` but leaves every rule's branch in `Classify`, so the paths the check counted are all still there. Calling `IsValueClass` *a separate question* does not hold: only the lcom4 branch asks it, and it has no caller of its own."
        },
        {
          "type": "claims",
          "caption": "gemma4's claims about the agent's reason",
          "claims": [
            {
              "text": "ClassifyAsync only adapts the contract to a Task.",
              "evidence": "After the fix ClassifyAsync holds two statements: the cancellation check and Task.FromResult(Classify(problem)).",
              "holds": true
            },
            {
              "text": "Classify is one rule per check.",
              "evidence": "Classify has one if per check: split-complexity, flat-dispatch and lcom4, and returns Unknown otherwise.",
              "holds": true
            },
            {
              "text": "IsValueClass is a separate question with its own caller.",
              "evidence": "ValueClassRule.IsValueClass has one caller, the lcom4 branch of Classify, which the method held before the fix.",
              "holds": false
            }
          ]
        },
        { "type": "facts", "caption": "What each position says", "agreement": true, "facts": [{"label":"The agent","value":"keep"},{"label":"gemma4","value":"replace"}] }
      ],
      "choices": [
        { "id": "agent", "label": "The agent is right" },
        { "id": "gemma4", "label": "gemma4 is right" }
      ]
    },
    {
      "id": "0000023",
      "repo": "/nowhere/registered",
      "title": "TagIndex.Add flagged as flat dispatch",
      "tags": ["flat-dispatch"],
      "created": "2026-11-01T09:40:00+01:00",
      "blocks": [
        { "type": "text", "caption": "Why the agent kept it", "text": "Each arm adds one tag kind; there is nothing to split." },
        { "type": "image", "caption": "A block this window does not know", "src": "screenshot.png" },
        { "type": "facts", "caption": "What each position says", "agreement": true, "facts": [{"label":"The agent","value":"keep"},{"label":"gemma4","value":"keep"}] }
      ],
      "choices": [
        { "id": "agent", "label": "The agent is right" },
        { "id": "gemma4", "label": "gemma4 is right" }
      ]
    },
    {
      "id": "0000031",
      "repo": "@REPO@",
      "title": "Pricing.Discount grew to cyclomatic complexity 9, over the limit of 8",
      "tags": [
        "cyclomatic"
      ],
      "created": "2026-11-01T10:05:00+01:00",
      "blocks": [
        {
          "type": "facts",
          "caption": "The check",
          "facts": [
            {
              "label": "Cyclomatic",
              "value": "9",
              "tone": "bad"
            },
            {
              "label": "Limit",
              "value": "8",
              "tone": "neutral"
            },
            {
              "label": "Before the change",
              "value": "5",
              "tone": "good"
            },
            {
              "label": "Commit",
              "value": "e4f5a6b"
            }
          ]
        },
        {
          "type": "code",
          "caption": "The method the check flagged",
          "path": "src/Shop/Pricing.cs",
          "startLine": 12,
          "code": "    public static decimal Discount(Order order, Customer customer)\n    {\n        if (order.Lines.Count == 0)\n        {\n            return 0m;\n        }\n\n        var rate = 0m;\n        if ((customer.IsMember || order.Coupon is not null) && order.Total != 12)\n        {\n            rate = 0.05m;\n        }\n\n        foreach (var line in order.Lines)\n        {\n            if (line.Quantity >= 10)\n            {\n                rate += 0.01m;\n            }\n        }\n\n        return customer.Country switch\n        {\n            \"NL\" => rate + 0.02m,\n            \"BE\" => rate + 0.01m,\n            _ => rate,\n        };\n    }",
          "marks": [
            {
              "start": { "line": 14, "col": 9 },
              "end": { "line": 14, "col": 36 },
              "label": "if",
              "weight": 1,
              "note": "one decision: an order without lines",
              "text": "if (order.Lines.Count == 0)"
            },
            {
              "start": { "line": 20, "col": 9 },
              "end": { "line": 20, "col": 82 },
              "label": "if",
              "weight": 1,
              "note": "one decision, whose condition holds two more",
              "text": "if ((customer.IsMember || order.Coupon is not null) && order.Total != 12)",
              "marks": [
                {
                  "start": { "line": 20, "col": 32 },
                  "end": { "line": 20, "col": 34 },
                  "label": "||",
                  "weight": 1,
                  "note": "a second way into the branch: a coupon without a membership",
                  "text": "||"
                },
                {
                  "start": { "line": 20, "col": 61 },
                  "end": { "line": 20, "col": 63 },
                  "label": "&&",
                  "weight": 1,
                  "note": "a third way past it: a total of 12 skips the branch",
                  "text": "&&"
                }
              ]
            },
            {
              "start": { "line": 25, "col": 9 },
              "end": { "line": 25, "col": 42 },
              "label": "foreach",
              "weight": 1,
              "note": "one loop: it runs zero times or more",
              "text": "foreach (var line in order.Lines)"
            },
            {
              "start": { "line": 27, "col": 13 },
              "end": { "line": 27, "col": 37 },
              "label": "if",
              "weight": 1,
              "note": "one decision inside the loop",
              "text": "if (line.Quantity >= 10)"
            },
            {
              "start": { "line": 35, "col": 13 },
              "end": { "line": 35, "col": 34 },
              "label": "arm",
              "weight": 1,
              "note": "a switch arm",
              "text": "\"NL\" => rate + 0.02m,"
            },
            {
              "start": { "line": 36, "col": 13 },
              "end": { "line": 36, "col": 34 },
              "label": "arm",
              "weight": 1,
              "note": "a switch arm",
              "text": "\"BE\" => rate + 0.01m,"
            },
            {
              "start": { "line": 37, "col": 13 },
              "end": { "line": 37, "col": 23 },
              "label": "default arm",
              "weight": 0,
              "note": "the default arm adds no path: the switch ends somewhere",
              "text": "_ => rate,"
            }
          ],
          "tally": {
            "label": "cyclomatic",
            "base": 1,
            "value": 9
          }
        },
        {
          "type": "timeline",
          "caption": "How the method came to 9",
          "steps": [
            {
              "label": "a1b2c3d, the method before",
              "when": "2026-10-20T14:30:00+02:00",
              "tone": "good",
              "blocks": [
                {
                  "type": "code",
                  "caption": "Cyclomatic 5",
                  "path": "src/Shop/Pricing.cs",
                  "startLine": 12,
                  "code": "    public static decimal Discount(Order order, Customer customer)\n    {\n        if (order.Lines.Count == 0)\n        {\n            return 0m;\n        }\n\n        var rate = customer.IsMember ? 0.05m : 0m;\n        return customer.Country switch\n        {\n            \"NL\" => rate + 0.02m,\n            \"BE\" => rate + 0.01m,\n            _ => rate,\n        };\n    }",
                  "marks": [
                    {
                      "start": { "line": 14, "col": 9 },
                      "end": { "line": 14, "col": 36 },
                      "label": "if",
                      "weight": 1,
                      "note": "one decision: an order without lines",
                      "text": "if (order.Lines.Count == 0)"
                    },
                    {
                      "start": { "line": 19, "col": 20 },
                      "end": { "line": 19, "col": 50 },
                      "label": "?:",
                      "weight": 1,
                      "note": "one decision: members get 5%",
                      "text": "customer.IsMember ? 0.05m : 0m"
                    },
                    {
                      "start": { "line": 22, "col": 13 },
                      "end": { "line": 22, "col": 34 },
                      "label": "arm",
                      "weight": 1,
                      "note": "a switch arm",
                      "text": "\"NL\" => rate + 0.02m,"
                    },
                    {
                      "start": { "line": 23, "col": 13 },
                      "end": { "line": 23, "col": 34 },
                      "label": "arm",
                      "weight": 1,
                      "note": "a switch arm",
                      "text": "\"BE\" => rate + 0.01m,"
                    },
                    {
                      "start": { "line": 24, "col": 13 },
                      "end": { "line": 24, "col": 23 },
                      "label": "default arm",
                      "weight": 0,
                      "note": "the default arm adds no path",
                      "text": "_ => rate,"
                    }
                  ],
                  "tally": {
                    "label": "cyclomatic",
                    "base": 1,
                    "value": 5
                  }
                }
              ]
            },
            {
              "label": "e4f5a6b, coupons and bulk lines",
              "when": "2026-10-30T09:12:00+01:00",
              "tone": "bad",
              "blocks": [
                {
                  "type": "diff",
                  "caption": "The change",
                  "files": [
                    {
                      "path": "src/Shop/Pricing.cs",
                      "before": "namespace Shop;\n\ninternal static class Pricing\n{\n    public static decimal Discount(Order order, Customer customer)\n    {\n        if (order.Lines.Count == 0)\n        {\n            return 0m;\n        }\n\n        var rate = customer.IsMember ? 0.05m : 0m;\n        return customer.Country switch\n        {\n            \"NL\" => rate + 0.02m,\n            \"BE\" => rate + 0.01m,\n            _ => rate,\n        };\n    }\n}\n",
                      "after": "namespace Shop;\n\ninternal static class Pricing\n{\n    public static decimal Discount(Order order, Customer customer)\n    {\n        if (order.Lines.Count == 0)\n        {\n            return 0m;\n        }\n\n        var rate = 0m;\n        if ((customer.IsMember || order.Coupon is not null) && order.Total != 12)\n        {\n            rate = 0.05m;\n        }\n\n        foreach (var line in order.Lines)\n        {\n            if (line.Quantity >= 10)\n            {\n                rate += 0.01m;\n            }\n        }\n\n        return customer.Country switch\n        {\n            \"NL\" => rate + 0.02m,\n            \"BE\" => rate + 0.01m,\n            _ => rate,\n        };\n    }\n}\n"
                    }
                  ]
                },
                {
                  "type": "text",
                  "caption": "The commit message",
                  "text": "Coupons count like a membership, and ten of one line earn 1% more."
                },
                { "type": "facts", "caption": "What each position says", "agreement": true, "facts": [{"label":"The agent","value":"keep"},{"label":"gemma4","value":"keep"}] },
                { "type": "facts", "caption": "What each position says", "agreement": true, "facts": [{"label":"The agent","value":"keep"},{"label":"gemma4","value":"replace"}] }
              ]
            }
          ]
        }
      ],
      "choices": [
        {
          "id": "split",
          "label": "Split it"
        },
        {
          "id": "keep",
          "label": "Keep it, the limit is wrong here"
        }
      ]
    },
    {
      "id": "0000032",
      "repo": "@REPO@",
      "title": "Pricing.Discount, counted by a check that missed the loop",
      "tags": [
        "cyclomatic"
      ],
      "created": "2026-11-01T10:06:00+01:00",
      "blocks": [
        {
          "type": "code",
          "caption": "The marks add up to 8, and the check says 9",
          "path": "src/Shop/Pricing.cs",
          "startLine": 12,
          "code": "    public static decimal Discount(Order order, Customer customer)\n    {\n        if (order.Lines.Count == 0)\n        {\n            return 0m;\n        }\n\n        var rate = 0m;\n        if ((customer.IsMember || order.Coupon is not null) && order.Total != 12)\n        {\n            rate = 0.05m;\n        }\n\n        foreach (var line in order.Lines)\n        {\n            if (line.Quantity >= 10)\n            {\n                rate += 0.01m;\n            }\n        }\n\n        return customer.Country switch\n        {\n            \"NL\" => rate + 0.02m,\n            \"BE\" => rate + 0.01m,\n            _ => rate,\n        };\n    }",
          "marks": [
            {
              "start": { "line": 14, "col": 9 },
              "end": { "line": 14, "col": 36 },
              "label": "if",
              "weight": 1,
              "note": "one decision: an order without lines",
              "text": "if (order.Lines.Count == 0)"
            },
            {
              "start": { "line": 20, "col": 9 },
              "end": { "line": 20, "col": 82 },
              "label": "if",
              "weight": 1,
              "note": "one decision, whose condition holds two more",
              "text": "if ((customer.IsMember || order.Coupon is not null) && order.Total != 12)",
              "marks": [
                {
                  "start": { "line": 20, "col": 32 },
                  "end": { "line": 20, "col": 34 },
                  "label": "||",
                  "weight": 1,
                  "note": "a second way into the branch: a coupon without a membership",
                  "text": "||"
                },
                {
                  "start": { "line": 20, "col": 61 },
                  "end": { "line": 20, "col": 63 },
                  "label": "&&",
                  "weight": 1,
                  "note": "a third way past it: a total of 12 skips the branch",
                  "text": "&&"
                }
              ]
            },
            {
              "start": { "line": 27, "col": 13 },
              "end": { "line": 27, "col": 37 },
              "label": "if",
              "weight": 1,
              "note": "one decision inside the loop",
              "text": "if (line.Quantity > 10)"
            },
            {
              "start": { "line": 35, "col": 13 },
              "end": { "line": 35, "col": 34 },
              "label": "arm",
              "weight": 1,
              "note": "a switch arm",
              "text": "\"NL\" => rate + 0.02m,"
            },
            {
              "start": { "line": 36, "col": 13 },
              "end": { "line": 36, "col": 34 },
              "label": "arm",
              "weight": 1,
              "note": "a switch arm",
              "text": "\"BE\" => rate + 0.01m,"
            },
            {
              "start": { "line": 37, "col": 13 },
              "end": { "line": 37, "col": 23 },
              "label": "default arm",
              "weight": 0,
              "note": "the default arm adds no path: the switch ends somewhere",
              "text": "_ => rate,"
            }
          ],
          "tally": {
            "label": "cyclomatic",
            "base": 1,
            "value": 9
          }
        },
        {
          "type": "timeline",
          "caption": "The check's earlier counts",
          "steps": [
            {
              "label": "a1b2c3d, counted 6",
              "when": "2026-10-20T14:30:00+02:00",
              "blocks": [
                {
                  "type": "code",
                  "caption": "The marks add up to 5, and the check said 6",
                  "path": "src/Shop/Pricing.cs",
                  "startLine": 12,
                  "code": "    public static decimal Discount(Order order, Customer customer)\n    {\n        if (order.Lines.Count == 0)\n        {\n            return 0m;\n        }\n\n        var rate = customer.IsMember ? 0.05m : 0m;\n        return customer.Country switch\n        {\n            \"NL\" => rate + 0.02m,\n            \"BE\" => rate + 0.01m,\n            _ => rate,\n        };\n    }",
                  "marks": [
                    {
                      "start": { "line": 14, "col": 9 },
                      "end": { "line": 14, "col": 36 },
                      "label": "if",
                      "weight": 1,
                      "note": "one decision: an order without lines",
                      "text": "if (order.Lines.Count == 0)"
                    },
                    {
                      "start": { "line": 19, "col": 20 },
                      "end": { "line": 19, "col": 50 },
                      "label": "?:",
                      "weight": 1,
                      "note": "one decision: members get 5%",
                      "text": "customer.IsMember ? 0.05m : 0m"
                    },
                    {
                      "start": { "line": 22, "col": 13 },
                      "end": { "line": 22, "col": 34 },
                      "label": "arm",
                      "weight": 1,
                      "note": "a switch arm",
                      "text": "\"NL\" => rate + 0.02m,"
                    },
                    {
                      "start": { "line": 23, "col": 13 },
                      "end": { "line": 23, "col": 34 },
                      "label": "arm",
                      "weight": 1,
                      "note": "a switch arm",
                      "text": "\"BE\" => rate + 0.01m,"
                    },
                    {
                      "start": { "line": 24, "col": 13 },
                      "end": { "line": 24, "col": 23 },
                      "label": "default arm",
                      "weight": 0,
                      "note": "the default arm adds no path",
                      "text": "_ => rate,"
                    }
                  ],
                  "tally": {
                    "label": "cyclomatic",
                    "base": 1,
                    "value": 6
                  }
                }
              ]
            },
            {
              "label": "e4f5a6b, counted 9",
              "when": "2026-10-30T09:12:00+01:00",
              "blocks": [
                {
                  "type": "text",
                  "caption": "The count above",
                  "text": "The check counted the method above as 9."
                }
              ]
            }
          ]
        }
      ],
      "choices": [
        {
          "id": "split",
          "label": "Split it"
        },
        {
          "id": "keep",
          "label": "Keep it"
        }
      ]
    }
  ]
}
EOF

printf '%s\n' "${questions//@REPO@/$repo}"
