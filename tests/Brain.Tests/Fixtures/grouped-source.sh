#!/usr/bin/env bash
# A source for brain's window whose questions come in groups, as Navarone's disputes do: two landings of three and two
# questions. They are what `navarone disputes --json` printed at navarone 47c1cec for one dispute and four accepted
# reasons: ids, titles, tags, blocks, choices and the focus lines as printed, with the repo made the test's. Navarone
# adds a landing, a commit, whether the code changed since and the query its lens opens on when it has saved a context
# for the question; these listings had none for most, so the fixture sets them: the first question opens on the query
# context-source.sh answers and carries Navarone's focus lines for it, and the second is about older code. The repo is
# the first argument, and the source's name the second, navarone when none is given.
#
#   bash grouped-source.sh /path/to/repo [name]
set -euo pipefail

repo="${1:?usage: grouped-source.sh <repo> [name]}"
name="${2:-navarone}"

read -r -d '' questions <<'EOF' || true
{
  "source": "@SOURCE@",
  "questions": [
    {
      "id": "7faddf9e01bb4d6fa873daeffe69c166",
      "repo": "@REPO@",
      "title": "0000001 cyclomatic Navarone.Classifiers::Navarone.Classifiers.Deterministic.DeterministicProblemClassifier.Classify(ProblemCase)",
      "tags": [
        "cyclomatic",
        "disputed"
      ],
      "created": "2026-11-02T15:33:04.6283437+00:00",
      "blocks": [
        {
          "type": "text",
          "caption": "what you judge",
          "text": "The agent defends its fix. The judge rejected its reason. Who is right: is the fix good?"
        },
        {
          "type": "facts",
          "caption": "positions",
          "facts": [
            {
              "label": "agent",
              "value": "keep",
              "tone": "good"
            },
            {
              "label": "the judge",
              "value": "replace",
              "tone": "bad"
            }
          ],
          "agreement": true
        },
        {
          "type": "text",
          "caption": "agent: why the fix is good",
          "text": "The judge read 'a flat dispatch for cyclomatic and cognitive' as a claim that Classify is a flat dispatch. The reason says the first rule returns a false positive when the subject is a flat dispatch, which is what line 28 tests: the check is cyclomatic or cognitive and the method's Dispatch is not None. Classify is three independent rules, one if each, no nesting, 11 paths from the tests each rule needs; splitting the rules into helpers would move the same tests, not remove them."
        },
        {
          "type": "text",
          "caption": "judge: why it isn't",
          "text": "The claim \"The first rule is a flat dispatch for cyclomatic and cognitive.\" does not hold: Line 28 checks `finding.Check is \"cyclomatic\" or \"cognitive\"`. The reason claims this is a 'flat dispatch'. However, the system prompt defines a flat dispatch as having 'every branch is a single return or a single expression and every condition is one test'. Here, the condition uses `or` (joining two tests) and `&&` (joining a second condition), and the subpattern `not Dispatch.None` adds complexity. More importantly, the 'flat dispatch' definition in the prompt is about the *structure of the branches* (single return/expression), which is true here, but the complexity comes from the *conditions*. The reason uses 'flat dispatch' to explain why the complexity is high, which is contradictory; flat dispatches are usually low complexity. However, the claim is that the rule *is* a flat dispatch. The code structure (if -> return) fits the structural definition. But the reason implies this structure *causes* the high complexity, which is misleading. Let's look closer at the 'flat dispatch' definition in the prompt: 'A claim that the code is a flat dispatch or a table holds only when every branch is a single return or a single expression and every condition is one test: a when, a property pattern, && or || makes a case or arm more than one test'. The condition here has `&&` and `or`. Therefore, it is **not** a flat dispatch according to the strict definition provided in the prompt. The reason claims it is a flat dispatch. This claim fails."
        },
        {
          "type": "claims",
          "caption": "the judge's claims",
          "claims": [
            {
              "text": "The method Classify holds three deterministic rules in sequence, one if per rule.",
              "evidence": "The code shows three `if` statements at lines 28, 33, and 38, each checking a specific `finding.Check` value (\"cyclomatic\"/\"cognitive\", \"control-coupling\", \"lcom4\").",
              "holds": true
            },
            {
              "text": "Each if returns its false positive at once.",
              "evidence": "Each of the three `if` blocks contains a `return new(ProblemVerdict.FalsePositive, ...)` statement immediately within the block.",
              "holds": true
            },
            {
              "text": "The first rule is a flat dispatch for cyclomatic and cognitive.",
              "evidence": "Line 28 checks `finding.Check is \"cyclomatic\" or \"cognitive\"`. The reason claims this is a 'flat dispatch'. However, the system prompt defines a flat dispatch as having 'every branch is a single return or a single expression and every condition is one test'. Here, the condition uses `or` (joining two tests) and `&&` (joining a second condition), and the subpattern `not Dispatch.None` adds complexity. More importantly, the 'flat dispatch' definition in the prompt is about the *structure of the branches* (single return/expression), which is true here, but the complexity comes from the *conditions*. The reason uses 'flat dispatch' to explain why the complexity is high, which is contradictory; flat dispatches are usually low complexity. However, the claim is that the rule *is* a flat dispatch. The code structure (if -> return) fits the structural definition. But the reason implies this structure *causes* the high complexity, which is misleading. Let's look closer at the 'flat dispatch' definition in the prompt: 'A claim that the code is a flat dispatch or a table holds only when every branch is a single return or a single expression and every condition is one test: a when, a property pattern, && or || makes a case or arm more than one test'. The condition here has `&&` and `or`. Therefore, it is **not** a flat dispatch according to the strict definition provided in the prompt. The reason claims it is a flat dispatch. This claim fails.",
              "holds": false
            },
            {
              "text": "The second rule is a mapping for control-coupling.",
              "evidence": "Line 33 checks `finding.Check == \"control-coupling\"`. The reason calls it a 'mapping'. The prompt defines a mapping as 'a flat dispatch whose every branch only returns a value'. Again, the condition has `&&` and a subpattern, so it is not a flat dispatch/mapping by the strict definition. The claim fails for the same reason as the previous one.",
              "holds": false
            },
            {
              "text": "The third rule is a value class check for lcom4.",
              "evidence": "Line 38 checks `finding.Check == \"lcom4\"` and `IsValueClass(type)`. This is a valid description of the logic.",
              "holds": true
            },
            {
              "text": "The 11 paths are three ifs of three or four tests each plus the undecided return at the end.",
              "evidence": "The evidence shows the complexity calculation: Base 1. If 1 adds 1 (if) + 1 (&&) + 1 (or) + 1 (subpattern) = 4. If 2 adds 1 (if) + 1 (&&) + 1 (subpattern) = 3. If 3 adds 1 (if) + 1 (&&) + 1 (&&) = 3. Total = 1 + 4 + 3 + 3 = 11. The reason says 'three ifs of three or four tests each'. If 1 has 4 additions, If 2 has 3, If 3 has 3. This matches 'three or four'. The 'undecided return' is the base path. This claim holds.",
              "holds": true
            },
            {
              "text": "No if is nested in another, and no rule reads the result of another.",
              "evidence": "The three `if` statements are sequential and independent. None are nested inside the others, and they do not depend on the results of the previous `if` statements (they all check `finding.Check` directly).",
              "holds": true
            }
          ]
        },
        {
          "type": "code",
          "caption": "the code now",
          "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
          "startLine": 24,
          "code": "    private static ProblemClassification Classify(ProblemCase problem)\n    {\n        var finding = problem.Finding;\n        var method = problem.Facts.Types.SelectMany(type => type.Methods).FirstOrDefault(method => method.Symbol == finding.Subject);\n        if (finding.Check is \"cyclomatic\" or \"cognitive\" && method is { Dispatch: not Dispatch.None })\n        {\n            return new(ProblemVerdict.FalsePositive, null, Classifier.Deterministic, \"the method is a flat dispatch: every branch is a single return or expression\");\n        }\n\n        if (finding.Check == \"control-coupling\" && method is { Dispatch: Dispatch.Mapping })\n        {\n            return new(ProblemVerdict.FalsePositive, null, Classifier.Deterministic, \"the method is a mapping: a flat dispatch whose every branch only returns a value\");\n        }\n\n        if (finding.Check == \"lcom4\" && problem.Facts.Types.FirstOrDefault(type => type.Symbol == finding.Subject) is { } type && IsValueClass(type))\n        {\n            return new(ProblemVerdict.FalsePositive, null, Classifier.Deterministic, \"the class is a value class: every method is a property accessor over its own field, so each piece is data, not a separate job\");\n        }\n\n        return new(ProblemVerdict.Undecided, null, Classifier.Deterministic, \"no deterministic rule decides this problem\");\n    }",
          "highlights": [],
          "marks": [
            {
              "start": {
                "line": 28,
                "col": 9
              },
              "end": {
                "line": 28,
                "col": 103
              },
              "label": "if",
              "weight": 1,
              "note": "a branch adds one path",
              "text": "if (finding.Check is \"cyclomatic\" or \"cognitive\" && method is { Dispatch: not Dispatch.None })",
              "marks": [
                {
                  "start": {
                    "line": 28,
                    "col": 13
                  },
                  "end": {
                    "line": 28,
                    "col": 102
                  },
                  "label": "&&",
                  "weight": 1,
                  "note": "an and adds one path: it joins a second condition",
                  "text": "finding.Check is \"cyclomatic\" or \"cognitive\" && method is { Dispatch: not Dispatch.None }",
                  "marks": [
                    {
                      "start": {
                        "line": 28,
                        "col": 30
                      },
                      "end": {
                        "line": 28,
                        "col": 57
                      },
                      "label": "or",
                      "weight": 1,
                      "note": "an or adds one path: it joins a second condition",
                      "text": "\"cyclomatic\" or \"cognitive\"",
                      "marks": []
                    },
                    {
                      "start": {
                        "line": 28,
                        "col": 73
                      },
                      "end": {
                        "line": 28,
                        "col": 100
                      },
                      "label": "subpattern",
                      "weight": 1,
                      "note": "a subpattern that tests a value adds one path: the match fails when the value does not fit",
                      "text": "Dispatch: not Dispatch.None",
                      "marks": []
                    }
                  ]
                }
              ]
            },
            {
              "start": {
                "line": 33,
                "col": 9
              },
              "end": {
                "line": 33,
                "col": 93
              },
              "label": "if",
              "weight": 1,
              "note": "a branch adds one path",
              "text": "if (finding.Check == \"control-coupling\" && method is { Dispatch: Dispatch.Mapping })",
              "marks": [
                {
                  "start": {
                    "line": 33,
                    "col": 13
                  },
                  "end": {
                    "line": 33,
                    "col": 92
                  },
                  "label": "&&",
                  "weight": 1,
                  "note": "an and adds one path: it joins a second condition",
                  "text": "finding.Check == \"control-coupling\" && method is { Dispatch: Dispatch.Mapping }",
                  "marks": [
                    {
                      "start": {
                        "line": 33,
                        "col": 64
                      },
                      "end": {
                        "line": 33,
                        "col": 90
                      },
                      "label": "subpattern",
                      "weight": 1,
                      "note": "a subpattern that tests a value adds one path: the match fails when the value does not fit",
                      "text": "Dispatch: Dispatch.Mapping",
                      "marks": []
                    }
                  ]
                }
              ]
            },
            {
              "start": {
                "line": 38,
                "col": 9
              },
              "end": {
                "line": 38,
                "col": 150
              },
              "label": "if",
              "weight": 1,
              "note": "a branch adds one path",
              "text": "if (finding.Check == \"lcom4\" && problem.Facts.Types.FirstOrDefault(type => type.Symbol == finding.Subject) is { } type && IsValueClass(type))",
              "marks": [
                {
                  "start": {
                    "line": 38,
                    "col": 13
                  },
                  "end": {
                    "line": 38,
                    "col": 149
                  },
                  "label": "&&",
                  "weight": 1,
                  "note": "an and adds one path: it joins a second condition",
                  "text": "finding.Check == \"lcom4\" && problem.Facts.Types.FirstOrDefault(type => type.Symbol == finding.Subject) is { } type && IsValueClass(type)",
                  "marks": [
                    {
                      "start": {
                        "line": 38,
                        "col": 13
                      },
                      "end": {
                        "line": 38,
                        "col": 127
                      },
                      "label": "&&",
                      "weight": 1,
                      "note": "an and adds one path: it joins a second condition",
                      "text": "finding.Check == \"lcom4\" && problem.Facts.Types.FirstOrDefault(type => type.Symbol == finding.Subject) is { } type",
                      "marks": []
                    }
                  ]
                }
              ]
            }
          ],
          "tally": {
            "label": "cyclomatic",
            "base": 1,
            "value": 11
          }
        },
        {
          "type": "diff",
          "caption": "the fix",
          "files": [
            {
              "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
              "before": "    private static ProblemClassification Classify(ProblemCase problem)\n    {\n        var finding = problem.Finding;\n        var method = problem.Facts.Types.SelectMany(type => type.Methods).FirstOrDefault(method => method.Symbol == finding.Subject);\n        if (finding.Check is \"cyclomatic\" or \"cognitive\" && method is { Dispatch: not Dispatch.None })\n        {\n            return new(ProblemVerdict.FalsePositive, null, Classifier.Deterministic, \"the method is a flat dispatch: every branch is a single return or expression\");\n        }\n\n        if (finding.Check == \"control-coupling\" && method is { Dispatch: Dispatch.Mapping })\n        {\n            return new(ProblemVerdict.FalsePositive, null, Classifier.Deterministic, \"the method is a mapping: a flat dispatch whose every branch only returns a value\");\n        }\n\n        if (finding.Check == \"lcom4\" && problem.Facts.Types.FirstOrDefault(type => type.Symbol == finding.Subject) is { } type && IsValueClass(type))\n        {\n            return new(ProblemVerdict.FalsePositive, null, Classifier.Deterministic, \"the class is a value class: every method is a property accessor over its own field, so each piece is data, not a separate job\");\n        }\n\n        return new(ProblemVerdict.Undecided, null, Classifier.Deterministic, \"no deterministic rule decides this problem\");\n    }",
              "after": "    private static ProblemClassification Classify(ProblemCase problem)\n    {\n        var finding = problem.Finding;\n        var method = problem.Facts.Types.SelectMany(type => type.Methods).FirstOrDefault(method => method.Symbol == finding.Subject);\n        if (finding.Check is \"cyclomatic\" or \"cognitive\" && method is { Dispatch: not Dispatch.None })\n        {\n            return new(ProblemVerdict.FalsePositive, null, Classifier.Deterministic, \"the method is a flat dispatch: every branch is a single return or expression\");\n        }\n\n        if (finding.Check == \"control-coupling\" && method is { Dispatch: Dispatch.Mapping })\n        {\n            return new(ProblemVerdict.FalsePositive, null, Classifier.Deterministic, \"the method is a mapping: a flat dispatch whose every branch only returns a value\");\n        }\n\n        if (finding.Check == \"lcom4\" && problem.Facts.Types.FirstOrDefault(type => type.Symbol == finding.Subject) is { } type && IsValueClass(type))\n        {\n            return new(ProblemVerdict.FalsePositive, null, Classifier.Deterministic, \"the class is a value class: every method is a property accessor over its own field, so each piece is data, not a separate job\");\n        }\n\n        return new(ProblemVerdict.Undecided, null, Classifier.Deterministic, \"no deterministic rule decides this problem\");\n    }"
            }
          ]
        },
        {
          "type": "facts",
          "caption": "the score",
          "facts": [
            {
              "label": "check",
              "value": "cyclomatic"
            },
            {
              "label": "value",
              "value": "11"
            },
            {
              "label": "limit",
              "value": "10"
            },
            {
              "label": "over the limit by",
              "value": "1"
            }
          ]
        },
        {
          "type": "timeline",
          "caption": "history",
          "steps": [
            {
              "label": "version 1",
              "when": "2026-11-02T14:52:08.8054916+00:00",
              "blocks": [
                {
                  "type": "facts",
                  "caption": "the version",
                  "facts": [
                    {
                      "label": "from",
                      "value": "saved"
                    },
                    {
                      "label": "score",
                      "value": "11"
                    },
                    {
                      "label": "when",
                      "value": "2026-11-02T14:52:08.8054916+00:00"
                    }
                  ]
                },
                {
                  "type": "code",
                  "caption": "the code",
                  "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
                  "startLine": 23,
                  "code": "    private static ProblemClassification Classify(ProblemCase problem)\n    {\n        var finding = problem.Finding;\n        var method = problem.Facts.Types.SelectMany(type => type.Methods).FirstOrDefault(method => method.Symbol == finding.Subject);\n        if (finding.Check is \"cyclomatic\" or \"cognitive\" && method is { Dispatch: not Dispatch.None })\n        {\n            return new(ProblemVerdict.FalsePositive, null, Classifier.Deterministic, \"the method is a flat dispatch: every branch is a single return or expression\");\n        }\n\n        if (finding.Check == \"control-coupling\" && method is { Dispatch: Dispatch.Mapping })\n        {\n            return new(ProblemVerdict.FalsePositive, null, Classifier.Deterministic, \"the method is a mapping: a flat dispatch whose every branch only returns a value\");\n        }\n\n        if (finding.Check == \"lcom4\" && problem.Facts.Types.FirstOrDefault(type => type.Symbol == finding.Subject) is { } type && IsValueClass(type))\n        {\n            return new(ProblemVerdict.FalsePositive, null, Classifier.Deterministic, \"the class is a value class: every method is a property accessor over its own field, so each piece is data, not a separate job\");\n        }\n\n        return new(ProblemVerdict.Undecided, null, Classifier.Deterministic, \"no deterministic rule decides this problem\");\n    }",
                  "highlights": [],
                  "marks": [
                    {
                      "start": {
                        "line": 27,
                        "col": 9
                      },
                      "end": {
                        "line": 27,
                        "col": 103
                      },
                      "label": "if",
                      "weight": 1,
                      "note": "a branch adds one path",
                      "text": "if (finding.Check is \"cyclomatic\" or \"cognitive\" && method is { Dispatch: not Dispatch.None })",
                      "marks": [
                        {
                          "start": {
                            "line": 27,
                            "col": 13
                          },
                          "end": {
                            "line": 27,
                            "col": 102
                          },
                          "label": "&&",
                          "weight": 1,
                          "note": "an and adds one path: it joins a second condition",
                          "text": "finding.Check is \"cyclomatic\" or \"cognitive\" && method is { Dispatch: not Dispatch.None }",
                          "marks": [
                            {
                              "start": {
                                "line": 27,
                                "col": 30
                              },
                              "end": {
                                "line": 27,
                                "col": 57
                              },
                              "label": "or",
                              "weight": 1,
                              "note": "an or adds one path: it joins a second condition",
                              "text": "\"cyclomatic\" or \"cognitive\"",
                              "marks": []
                            },
                            {
                              "start": {
                                "line": 27,
                                "col": 73
                              },
                              "end": {
                                "line": 27,
                                "col": 100
                              },
                              "label": "subpattern",
                              "weight": 1,
                              "note": "a subpattern that tests a value adds one path: the match fails when the value does not fit",
                              "text": "Dispatch: not Dispatch.None",
                              "marks": []
                            }
                          ]
                        }
                      ]
                    },
                    {
                      "start": {
                        "line": 32,
                        "col": 9
                      },
                      "end": {
                        "line": 32,
                        "col": 93
                      },
                      "label": "if",
                      "weight": 1,
                      "note": "a branch adds one path",
                      "text": "if (finding.Check == \"control-coupling\" && method is { Dispatch: Dispatch.Mapping })",
                      "marks": [
                        {
                          "start": {
                            "line": 32,
                            "col": 13
                          },
                          "end": {
                            "line": 32,
                            "col": 92
                          },
                          "label": "&&",
                          "weight": 1,
                          "note": "an and adds one path: it joins a second condition",
                          "text": "finding.Check == \"control-coupling\" && method is { Dispatch: Dispatch.Mapping }",
                          "marks": [
                            {
                              "start": {
                                "line": 32,
                                "col": 64
                              },
                              "end": {
                                "line": 32,
                                "col": 90
                              },
                              "label": "subpattern",
                              "weight": 1,
                              "note": "a subpattern that tests a value adds one path: the match fails when the value does not fit",
                              "text": "Dispatch: Dispatch.Mapping",
                              "marks": []
                            }
                          ]
                        }
                      ]
                    },
                    {
                      "start": {
                        "line": 37,
                        "col": 9
                      },
                      "end": {
                        "line": 37,
                        "col": 150
                      },
                      "label": "if",
                      "weight": 1,
                      "note": "a branch adds one path",
                      "text": "if (finding.Check == \"lcom4\" && problem.Facts.Types.FirstOrDefault(type => type.Symbol == finding.Subject) is { } type && IsValueClass(type))",
                      "marks": [
                        {
                          "start": {
                            "line": 37,
                            "col": 13
                          },
                          "end": {
                            "line": 37,
                            "col": 149
                          },
                          "label": "&&",
                          "weight": 1,
                          "note": "an and adds one path: it joins a second condition",
                          "text": "finding.Check == \"lcom4\" && problem.Facts.Types.FirstOrDefault(type => type.Symbol == finding.Subject) is { } type && IsValueClass(type)",
                          "marks": [
                            {
                              "start": {
                                "line": 37,
                                "col": 13
                              },
                              "end": {
                                "line": 37,
                                "col": 127
                              },
                              "label": "&&",
                              "weight": 1,
                              "note": "an and adds one path: it joins a second condition",
                              "text": "finding.Check == \"lcom4\" && problem.Facts.Types.FirstOrDefault(type => type.Symbol == finding.Subject) is { } type",
                              "marks": []
                            }
                          ]
                        }
                      ]
                    }
                  ],
                  "tally": {
                    "label": "cyclomatic",
                    "base": 1,
                    "value": 11
                  }
                },
                {
                  "type": "text",
                  "caption": "reason: rejected by the deterministic classifier",
                  "text": "\n\nwhy: the reason has 0 words; give at least 8"
                },
                {
                  "type": "facts",
                  "caption": "positions",
                  "facts": [
                    {
                      "label": "agent",
                      "value": "keep",
                      "tone": "good"
                    },
                    {
                      "label": "the deterministic classifier",
                      "value": "replace",
                      "tone": "bad"
                    }
                  ],
                  "agreement": true
                },
                {
                  "type": "text",
                  "caption": "reason: for the user, judged an attempt to fool the gate by the judge",
                  "text": "Classify holds the three deterministic rules that SPEC.md names under Classifying a problem, one if per rule, each returning its false positive: a flat dispatch for cyclomatic and cognitive, a mapping for control-coupling, and a value class for lcom4. Each if tests the check and one fact of its subject, so the 11 paths are three ifs of three or four tests each plus the undecided fall-through. The rules do not depend on each other and the method has no nesting; moving each rule into a helper would keep the same tests in one split.\n\nwhy: The claim \"Moving each rule into a helper would keep the same tests in one split.\" does not hold: This is a claim about a potential refactor. The code shows the logic is self-contained within each if block (returning immediately). Extracting them would not change the number of paths in the original method if the method still called them, but the claim is about the *reason* for the complexity. The complexity exists because the conditions are complex. The claim is vague and doesn't explain why the *current* code needs 11 paths, but rather suggests a fix. However, the prompt asks to judge the reason for *leaving* the problem. The reason given is that the rules are independent and flat. The claim about the helper is a suggestion, not an explanation of the current state's necessity. But more importantly, the reason states \"the 11 paths are three ifs of three or four tests each plus the undecided fall-through\". This is an accurate description of the structure. The claim about the helper is a non-sequitur or a weak justification for *not* fixing it (implying it's not worth it or it's a design choice). However, the core of the reason is the structural description. Let's look at the 'fooling' criteria: 'points at... a later refactor'. The reason says 'moving each rule into a helper would keep the same tests in one split'. This is pointing to a refactor as a justification or context. But does it explain why the *current* code needs 11 paths? It explains the structure. The 'fooling' category includes 'points at... a later refactor'. The reason explicitly discusses moving to a helper. This fits the 'fooling' criteria of pointing to a refactor. Furthermore, the claim 'moving each rule into a helper would keep the same tests in one split' is technically incorrect or misleading. If you move the logic to a helper, the `Classify` method would have 3 calls (or 1 if combined), reducing its complexity. The complexity would move to the helpers. It doesn't 'keep the same tests in one split' in a way that justifies the high complexity of `Classify`. It suggests the complexity is inherent to the rules, but the phrasing is awkward. More critically, the reason is largely a description of the code structure which is accurate, but the final sentence points to a refactor. Let's re-read the 'fooling' definition: 'points at deadlines, tests, reviews, generators, other code or a later refactor'. The reason says 'moving each rule into a helper...'. This is pointing to a refactor. Therefore, it is fooling."
                },
                {
                  "type": "facts",
                  "caption": "positions",
                  "facts": [
                    {
                      "label": "agent",
                      "value": "keep",
                      "tone": "good"
                    },
                    {
                      "label": "the judge",
                      "value": "replace",
                      "tone": "bad"
                    }
                  ],
                  "agreement": true
                },
                {
                  "type": "text",
                  "caption": "reason: for the user, judged an attempt to fool the gate by the judge",
                  "text": "Classify holds three deterministic rules in sequence, one if per rule, and each if returns its false positive at once: a flat dispatch for cyclomatic and cognitive, a mapping for control-coupling, and a value class for lcom4. Each if tests the check and one fact of the subject, so the 11 paths are three ifs of three or four tests each plus the undecided return at the end. No if is nested in another, and no rule reads the result of another.\n\nwhy: The claim \"The first rule is a flat dispatch for cyclomatic and cognitive.\" does not hold: Line 28 checks `finding.Check is \"cyclomatic\" or \"cognitive\"`. The reason claims this is a 'flat dispatch'. However, the system prompt defines a flat dispatch as having 'every branch is a single return or a single expression and every condition is one test'. Here, the condition uses `or` (joining two tests) and `&&` (joining a second condition), and the subpattern `not Dispatch.None` adds complexity. More importantly, the 'flat dispatch' definition in the prompt is about the *structure of the branches* (single return/expression), which is true here, but the complexity comes from the *conditions*. The reason uses 'flat dispatch' to explain why the complexity is high, which is contradictory; flat dispatches are usually low complexity. However, the claim is that the rule *is* a flat dispatch. The code structure (if -> return) fits the structural definition. But the reason implies this structure *causes* the high complexity, which is misleading. Let's look closer at the 'flat dispatch' definition in the prompt: 'A claim that the code is a flat dispatch or a table holds only when every branch is a single return or a single expression and every condition is one test: a when, a property pattern, && or || makes a case or arm more than one test'. The condition here has `&&` and `or`. Therefore, it is **not** a flat dispatch according to the strict definition provided in the prompt. The reason claims it is a flat dispatch. This claim fails."
                },
                {
                  "type": "facts",
                  "caption": "positions",
                  "facts": [
                    {
                      "label": "agent",
                      "value": "keep",
                      "tone": "good"
                    },
                    {
                      "label": "the judge",
                      "value": "replace",
                      "tone": "bad"
                    }
                  ],
                  "agreement": true
                }
              ]
            }
          ]
        }
      ],
      "choices": [
        {
          "id": "accept",
          "label": "Keep: the fix is good"
        },
        {
          "id": "reject",
          "label": "Replace: the fix is not good"
        }
      ],
      "stale": false,
      "focus": [
        {
          "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
          "lines": [
            28,
            33,
            38
          ]
        }
      ],
      "group": "master",
      "commit": "2c0e96878c4c1f0e9b7d3a5c8e2f4a6b1d3c5e7f",
      "context": "method DeterministicProblemClassifier.Classify class:short expand:1 [class:short]"
    },
    {
      "id": "accepted-505cdeafff9baf37cb94c77221402691",
      "repo": "@REPO@",
      "title": "0000004 cyclomatic navarone::Navarone.Cli.Commands.RunAsync(string[], string, NavaroneHome, Func<string, NavaroneHome, EngineSetup>, Terminal, TextWriter, TextWriter)",
      "tags": [
        "cyclomatic",
        "accepted"
      ],
      "created": "2026-11-02T15:23:28.8135464+00:00",
      "blocks": [
        {
          "type": "text",
          "caption": "what you judge",
          "text": "The agent wants to keep this code as it is, for the reason below. The judge agreed. Does the reason justify keeping it?"
        },
        {
          "type": "facts",
          "caption": "positions",
          "facts": [
            {
              "label": "agent",
              "value": "keep",
              "tone": "good"
            },
            {
              "label": "the judge",
              "value": "keep",
              "tone": "good"
            }
          ],
          "agreement": true
        },
        {
          "type": "text",
          "caption": "agent: the reason",
          "text": "RunAsync is the parser of the one-command CLI that SPEC.md's CLI section defines: one switch arm per command form, each a list pattern of literal words that hands its form to its own method, plus the catch that turns an unreadable navarone.json or reasons.json into a usage error. The 13 paths are the 11 forms and the catch. No arm holds a when, a property pattern or logic of its own, and the forms cannot be fewer without changing the CLI."
        },
        {
          "type": "text",
          "caption": "judge: why it holds",
          "text": "Held: The method contains a switch statement with 11 arms that add to the cyclomatic complexity.; The method contains a catch block that adds to the cyclomatic complexity.; The total cyclomatic complexity is 13, derived from the base of 1, the 11 switch arms, and the 1 catch block.; No switch arm contains a `when` clause, property pattern, or additional logic that would increase complexity beyond the arm itself.; The 13 paths correspond to the 11 command forms and the catch block.. The reason correctly identifies that the 13 paths are necessitated by the 11 distinct CLI command forms and the error handling catch block, which are inherent to the method's role as a CLI dispatcher."
        },
        {
          "type": "claims",
          "caption": "the judge's claims",
          "claims": [
            {
              "text": "The method contains a switch statement with 11 arms that add to the cyclomatic complexity.",
              "evidence": "The code shows a `switch` on `args` (line 28) with 11 specific pattern arms (lines 30-40) and one default arm (line 41). The Navarone proof confirms 11 arms each adding 1 to the complexity.",
              "holds": true
            },
            {
              "text": "The method contains a catch block that adds to the cyclomatic complexity.",
              "evidence": "The code shows a `catch (InvalidDataException exception)` block (line 44). The Navarone proof confirms this catch adds 1 to the complexity.",
              "holds": true
            },
            {
              "text": "The total cyclomatic complexity is 13, derived from the base of 1, the 11 switch arms, and the 1 catch block.",
              "evidence": "Navarone's finding states the value is 13 with a base of 1. The proof shows 11 arms adding 1 each and 1 catch adding 1, totaling 1 + 11 + 1 = 13.",
              "holds": true
            },
            {
              "text": "No switch arm contains a `when` clause, property pattern, or additional logic that would increase complexity beyond the arm itself.",
              "evidence": "The code shows each arm is a simple list pattern (e.g., `[\"disputes\", \"--json\"]`) or a variable pattern (e.g., `[var id, \"--accept\"]`) followed by a single return statement. There are no `when` clauses or nested conditional logic within the arms.",
              "holds": true
            },
            {
              "text": "The 13 paths correspond to the 11 command forms and the catch block.",
              "evidence": "The 11 arms represent 11 distinct command forms (including the empty array `[]`), and the catch block represents the error handling path. This matches the count of 13.",
              "holds": true
            }
          ]
        },
        {
          "type": "code",
          "caption": "the code now",
          "path": "src/Navarone.Cli/Commands.cs",
          "startLine": 21,
          "code": "    public static async Task<int> RunAsync(string[] args, string currentDirectory, NavaroneHome home, Func<string, NavaroneHome, EngineSetup> wiring, Terminal terminal, TextWriter output, TextWriter error)\n    {\n        var user = new User(terminal, output, error);\n        try\n        {\n            var repoRoot = FileLocations.RepoRoot(currentDirectory);\n            Task<int> inRepo(Func<EngineSetup, IReadOnlyList<string>, Task<int>> run) => InRepoAsync(wiring(repoRoot, home), error, run);\n            return args switch\n            {\n                [] => await inRepo((setup, codebases) => CheckAsync(setup, codebases, output)).ConfigureAwait(false),\n                [\"disputes\", \"--json\"] => await DisputeQuestions.WriteAsync(home, output).ConfigureAwait(false),\n                [\"evidence\", var id, \"--json\"] => await inRepo((setup, codebases) => EvidenceAsync(setup, codebases, new ProblemId(id), output, error)).ConfigureAwait(false),\n                [\"history\", var id, \"--json\"] => await inRepo((setup, codebases) => HistoryAsync(setup, codebases, new ProblemId(id), output, error)).ConfigureAwait(false),\n                [\"disputes\", \"--save\"] => await inRepo((setup, codebases) => SaveAcceptedAsync(setup, codebases, output)).ConfigureAwait(false),\n                [var id, \"--dispute\", var argument] => await inRepo((setup, codebases) => DisputeAsync(setup, codebases, new ProblemId(id), argument, output, error)).ConfigureAwait(false),\n                [var id, \"--accept\"] => await LabelAsync(repoRoot, home, new ProblemId(id), new ReasonLabel(ReasonVerdict.Real), user).ConfigureAwait(false),\n                [var id, \"--reject\"] => await LabelAsync(repoRoot, home, new ProblemId(id), new ReasonLabel(ReasonVerdict.Rejected), user).ConfigureAwait(false),\n                [var id, \"--real\"] => await LabelAsync(repoRoot, new ProblemId(id), new ProblemLabel(ProblemVerdict.Real, null), user).ConfigureAwait(false),\n                [var id, \"--false-positive\"] => await LabelAsync(repoRoot, new ProblemId(id), new ProblemLabel(ProblemVerdict.FalsePositive, null), user).ConfigureAwait(false),\n                [var id, var reason] => await inRepo((setup, codebases) => JudgeAsync(setup, codebases, new ProblemId(id), reason, output, error)).ConfigureAwait(false),\n                _ => await UsageAsync(error).ConfigureAwait(false),\n            };\n        }\n        catch (InvalidDataException exception)\n        {\n            await error.WriteLineAsync($\"navarone: {exception.Message}\").ConfigureAwait(false);\n            return ExitCodes.UsageError;\n        }\n    }",
          "highlights": [],
          "marks": [
            {
              "start": {
                "line": 28,
                "col": 20
              },
              "end": {
                "line": 28,
                "col": 31
              },
              "label": "switch",
              "weight": 0,
              "note": "a switch adds no path of its own: each of its cases counts",
              "text": "args switch",
              "marks": []
            },
            {
              "start": {
                "line": 30,
                "col": 17
              },
              "end": {
                "line": 30,
                "col": 19
              },
              "label": "arm",
              "weight": 1,
              "note": "a case adds one path; part of the switch at line 28",
              "text": "[]",
              "marks": []
            },
            {
              "start": {
                "line": 31,
                "col": 17
              },
              "end": {
                "line": 31,
                "col": 39
              },
              "label": "arm",
              "weight": 1,
              "note": "a case adds one path; part of the switch at line 28",
              "text": "[\"disputes\", \"--json\"]",
              "marks": []
            },
            {
              "start": {
                "line": 32,
                "col": 17
              },
              "end": {
                "line": 32,
                "col": 47
              },
              "label": "arm",
              "weight": 1,
              "note": "a case adds one path; part of the switch at line 28",
              "text": "[\"evidence\", var id, \"--json\"]",
              "marks": []
            },
            {
              "start": {
                "line": 33,
                "col": 17
              },
              "end": {
                "line": 33,
                "col": 46
              },
              "label": "arm",
              "weight": 1,
              "note": "a case adds one path; part of the switch at line 28",
              "text": "[\"history\", var id, \"--json\"]",
              "marks": []
            },
            {
              "start": {
                "line": 34,
                "col": 17
              },
              "end": {
                "line": 34,
                "col": 39
              },
              "label": "arm",
              "weight": 1,
              "note": "a case adds one path; part of the switch at line 28",
              "text": "[\"disputes\", \"--save\"]",
              "marks": []
            },
            {
              "start": {
                "line": 35,
                "col": 17
              },
              "end": {
                "line": 35,
                "col": 52
              },
              "label": "arm",
              "weight": 1,
              "note": "a case adds one path; part of the switch at line 28",
              "text": "[var id, \"--dispute\", var argument]",
              "marks": []
            },
            {
              "start": {
                "line": 36,
                "col": 17
              },
              "end": {
                "line": 36,
                "col": 37
              },
              "label": "arm",
              "weight": 1,
              "note": "a case adds one path; part of the switch at line 28",
              "text": "[var id, \"--accept\"]",
              "marks": []
            },
            {
              "start": {
                "line": 37,
                "col": 17
              },
              "end": {
                "line": 37,
                "col": 37
              },
              "label": "arm",
              "weight": 1,
              "note": "a case adds one path; part of the switch at line 28",
              "text": "[var id, \"--reject\"]",
              "marks": []
            },
            {
              "start": {
                "line": 38,
                "col": 17
              },
              "end": {
                "line": 38,
                "col": 35
              },
              "label": "arm",
              "weight": 1,
              "note": "a case adds one path; part of the switch at line 28",
              "text": "[var id, \"--real\"]",
              "marks": []
            },
            {
              "start": {
                "line": 39,
                "col": 17
              },
              "end": {
                "line": 39,
                "col": 45
              },
              "label": "arm",
              "weight": 1,
              "note": "a case adds one path; part of the switch at line 28",
              "text": "[var id, \"--false-positive\"]",
              "marks": []
            },
            {
              "start": {
                "line": 40,
                "col": 17
              },
              "end": {
                "line": 40,
                "col": 37
              },
              "label": "arm",
              "weight": 1,
              "note": "a case adds one path; part of the switch at line 28",
              "text": "[var id, var reason]",
              "marks": []
            },
            {
              "start": {
                "line": 41,
                "col": 17
              },
              "end": {
                "line": 41,
                "col": 18
              },
              "label": "_",
              "weight": 0,
              "note": "a default case adds no path: it is the way through when no case matches; part of the switch at line 28",
              "text": "_",
              "marks": []
            },
            {
              "start": {
                "line": 44,
                "col": 9
              },
              "end": {
                "line": 44,
                "col": 47
              },
              "label": "catch",
              "weight": 1,
              "note": "a catch adds one path",
              "text": "catch (InvalidDataException exception)",
              "marks": []
            }
          ],
          "tally": {
            "label": "cyclomatic",
            "base": 1,
            "value": 13
          }
        },
        {
          "type": "facts",
          "caption": "the score",
          "facts": [
            {
              "label": "check",
              "value": "cyclomatic"
            },
            {
              "label": "value",
              "value": "13"
            },
            {
              "label": "limit",
              "value": "10"
            },
            {
              "label": "over the limit by",
              "value": "3"
            }
          ]
        },
        {
          "type": "timeline",
          "caption": "history",
          "steps": [
            {
              "label": "version 1",
              "when": "2026-11-02T14:52:08.8101467+00:00",
              "blocks": [
                {
                  "type": "facts",
                  "caption": "the version",
                  "facts": [
                    {
                      "label": "from",
                      "value": "saved"
                    },
                    {
                      "label": "score",
                      "value": "13"
                    },
                    {
                      "label": "when",
                      "value": "2026-11-02T14:52:08.8101467+00:00"
                    }
                  ]
                },
                {
                  "type": "code",
                  "caption": "the code",
                  "path": "src/Navarone.Cli/Commands.cs",
                  "startLine": 20,
                  "code": "    public static async Task<int> RunAsync(string[] args, string currentDirectory, NavaroneHome home, Func<string, NavaroneHome, EngineSetup> wiring, Terminal terminal, TextWriter output, TextWriter error)\n    {\n        var user = new User(terminal, output, error);\n        try\n        {\n            var repoRoot = FileLocations.RepoRoot(currentDirectory);\n            Task<int> inRepo(Func<EngineSetup, IReadOnlyList<string>, Task<int>> run) => InRepoAsync(wiring(repoRoot, home), error, run);\n            return args switch\n            {\n                [] => await inRepo((setup, codebases) => CheckAsync(setup, codebases, output)).ConfigureAwait(false),\n                [\"disputes\", \"--json\"] => await DisputeQuestions.WriteAsync(home, output).ConfigureAwait(false),\n                [\"evidence\", var id, \"--json\"] => await inRepo((setup, codebases) => EvidenceAsync(setup, codebases, new ProblemId(id), output, error)).ConfigureAwait(false),\n                [\"history\", var id, \"--json\"] => await inRepo((setup, codebases) => HistoryAsync(setup, codebases, new ProblemId(id), output, error)).ConfigureAwait(false),\n                [\"disputes\", \"--save\"] => await inRepo((setup, codebases) => SaveAcceptedAsync(setup, codebases, output)).ConfigureAwait(false),\n                [var id, \"--dispute\", var argument] => await inRepo((setup, codebases) => DisputeAsync(setup, codebases, new ProblemId(id), argument, output, error)).ConfigureAwait(false),\n                [var id, \"--accept\"] => await LabelAsync(repoRoot, home, new ProblemId(id), new ReasonLabel(ReasonVerdict.Real), user).ConfigureAwait(false),\n                [var id, \"--reject\"] => await LabelAsync(repoRoot, home, new ProblemId(id), new ReasonLabel(ReasonVerdict.Rejected), user).ConfigureAwait(false),\n                [var id, \"--real\"] => await LabelAsync(repoRoot, new ProblemId(id), new ProblemLabel(ProblemVerdict.Real, null), user).ConfigureAwait(false),\n                [var id, \"--false-positive\"] => await LabelAsync(repoRoot, new ProblemId(id), new ProblemLabel(ProblemVerdict.FalsePositive, null), user).ConfigureAwait(false),\n                [var id, var reason] => await inRepo((setup, codebases) => JudgeAsync(setup, codebases, new ProblemId(id), reason, output, error)).ConfigureAwait(false),\n                _ => await UsageAsync(error).ConfigureAwait(false),\n            };\n        }\n        catch (InvalidDataException exception)\n        {\n            await error.WriteLineAsync($\"navarone: {exception.Message}\").ConfigureAwait(false);\n            return ExitCodes.UsageError;\n        }\n    }",
                  "highlights": [],
                  "marks": [
                    {
                      "start": {
                        "line": 27,
                        "col": 20
                      },
                      "end": {
                        "line": 27,
                        "col": 31
                      },
                      "label": "switch",
                      "weight": 0,
                      "note": "a switch adds no path of its own: each of its cases counts",
                      "text": "args switch",
                      "marks": []
                    },
                    {
                      "start": {
                        "line": 29,
                        "col": 17
                      },
                      "end": {
                        "line": 29,
                        "col": 19
                      },
                      "label": "arm",
                      "weight": 1,
                      "note": "a case adds one path; part of the switch at line 27",
                      "text": "[]",
                      "marks": []
                    },
                    {
                      "start": {
                        "line": 30,
                        "col": 17
                      },
                      "end": {
                        "line": 30,
                        "col": 39
                      },
                      "label": "arm",
                      "weight": 1,
                      "note": "a case adds one path; part of the switch at line 27",
                      "text": "[\"disputes\", \"--json\"]",
                      "marks": []
                    },
                    {
                      "start": {
                        "line": 31,
                        "col": 17
                      },
                      "end": {
                        "line": 31,
                        "col": 47
                      },
                      "label": "arm",
                      "weight": 1,
                      "note": "a case adds one path; part of the switch at line 27",
                      "text": "[\"evidence\", var id, \"--json\"]",
                      "marks": []
                    },
                    {
                      "start": {
                        "line": 32,
                        "col": 17
                      },
                      "end": {
                        "line": 32,
                        "col": 46
                      },
                      "label": "arm",
                      "weight": 1,
                      "note": "a case adds one path; part of the switch at line 27",
                      "text": "[\"history\", var id, \"--json\"]",
                      "marks": []
                    },
                    {
                      "start": {
                        "line": 33,
                        "col": 17
                      },
                      "end": {
                        "line": 33,
                        "col": 39
                      },
                      "label": "arm",
                      "weight": 1,
                      "note": "a case adds one path; part of the switch at line 27",
                      "text": "[\"disputes\", \"--save\"]",
                      "marks": []
                    },
                    {
                      "start": {
                        "line": 34,
                        "col": 17
                      },
                      "end": {
                        "line": 34,
                        "col": 52
                      },
                      "label": "arm",
                      "weight": 1,
                      "note": "a case adds one path; part of the switch at line 27",
                      "text": "[var id, \"--dispute\", var argument]",
                      "marks": []
                    },
                    {
                      "start": {
                        "line": 35,
                        "col": 17
                      },
                      "end": {
                        "line": 35,
                        "col": 37
                      },
                      "label": "arm",
                      "weight": 1,
                      "note": "a case adds one path; part of the switch at line 27",
                      "text": "[var id, \"--accept\"]",
                      "marks": []
                    },
                    {
                      "start": {
                        "line": 36,
                        "col": 17
                      },
                      "end": {
                        "line": 36,
                        "col": 37
                      },
                      "label": "arm",
                      "weight": 1,
                      "note": "a case adds one path; part of the switch at line 27",
                      "text": "[var id, \"--reject\"]",
                      "marks": []
                    },
                    {
                      "start": {
                        "line": 37,
                        "col": 17
                      },
                      "end": {
                        "line": 37,
                        "col": 35
                      },
                      "label": "arm",
                      "weight": 1,
                      "note": "a case adds one path; part of the switch at line 27",
                      "text": "[var id, \"--real\"]",
                      "marks": []
                    },
                    {
                      "start": {
                        "line": 38,
                        "col": 17
                      },
                      "end": {
                        "line": 38,
                        "col": 45
                      },
                      "label": "arm",
                      "weight": 1,
                      "note": "a case adds one path; part of the switch at line 27",
                      "text": "[var id, \"--false-positive\"]",
                      "marks": []
                    },
                    {
                      "start": {
                        "line": 39,
                        "col": 17
                      },
                      "end": {
                        "line": 39,
                        "col": 37
                      },
                      "label": "arm",
                      "weight": 1,
                      "note": "a case adds one path; part of the switch at line 27",
                      "text": "[var id, var reason]",
                      "marks": []
                    },
                    {
                      "start": {
                        "line": 40,
                        "col": 17
                      },
                      "end": {
                        "line": 40,
                        "col": 18
                      },
                      "label": "_",
                      "weight": 0,
                      "note": "a default case adds no path: it is the way through when no case matches; part of the switch at line 27",
                      "text": "_",
                      "marks": []
                    },
                    {
                      "start": {
                        "line": 43,
                        "col": 9
                      },
                      "end": {
                        "line": 43,
                        "col": 47
                      },
                      "label": "catch",
                      "weight": 1,
                      "note": "a catch adds one path",
                      "text": "catch (InvalidDataException exception)",
                      "marks": []
                    }
                  ],
                  "tally": {
                    "label": "cyclomatic",
                    "base": 1,
                    "value": 13
                  }
                },
                {
                  "type": "text",
                  "caption": "reason: rejected by the deterministic classifier",
                  "text": "\n\nwhy: the reason has 0 words; give at least 8"
                },
                {
                  "type": "facts",
                  "caption": "positions",
                  "facts": [
                    {
                      "label": "agent",
                      "value": "keep",
                      "tone": "good"
                    },
                    {
                      "label": "the deterministic classifier",
                      "value": "replace",
                      "tone": "bad"
                    }
                  ],
                  "agreement": true
                },
                {
                  "type": "text",
                  "caption": "reason: accepted by the judge",
                  "text": "RunAsync is the parser of the one-command CLI that SPEC.md's CLI section defines: one switch arm per command form, each a list pattern of literal words that hands its form to its own method, plus the catch that turns an unreadable navarone.json or reasons.json into a usage error. The 13 paths are the 11 forms and the catch. No arm holds a when, a property pattern or logic of its own, and the forms cannot be fewer without changing the CLI.\n\nwhy: Held: The method contains a switch statement with 11 arms that add to the cyclomatic complexity.; The method contains a catch block that adds to the cyclomatic complexity.; The total cyclomatic complexity is 13, derived from the base of 1, the 11 switch arms, and the 1 catch block.; No switch arm contains a `when` clause, property pattern, or additional logic that would increase complexity beyond the arm itself.; The 13 paths correspond to the 11 command forms and the catch block.. The reason correctly identifies that the 13 paths are necessitated by the 11 distinct CLI command forms and the error handling catch block, which are inherent to the method's role as a CLI dispatcher."
                },
                {
                  "type": "facts",
                  "caption": "positions",
                  "facts": [
                    {
                      "label": "agent",
                      "value": "keep",
                      "tone": "good"
                    },
                    {
                      "label": "the judge",
                      "value": "keep",
                      "tone": "good"
                    }
                  ],
                  "agreement": true
                }
              ]
            }
          ]
        }
      ],
      "choices": [
        {
          "id": "accept",
          "label": "Keep: the reason holds"
        },
        {
          "id": "reject",
          "label": "Replace: the reason does not hold"
        }
      ],
      "stale": true,
      "focus": [
        {
          "path": "src/Navarone.Cli/Commands.cs",
          "lines": [
            28,
            30,
            31,
            32,
            33,
            34,
            35,
            36,
            37,
            38,
            39,
            40,
            41,
            44
          ]
        }
      ],
      "group": "master",
      "commit": "2c0e96878c4c1f0e9b7d3a5c8e2f4a6b1d3c5e7f",
      "context": "method Commands.RunAsync class:short callers:3"
    },
    {
      "id": "accepted-439b7579ed926ead43c665d769d0204f",
      "repo": "@REPO@",
      "title": "0000016 lcom4 Navarone.Engine::Navarone.Engine.Reasons.ReasonsFile.ValueConverter<T>",
      "tags": [
        "lcom4",
        "accepted"
      ],
      "created": "2026-11-01T11:38:00.5658135+00:00",
      "blocks": [
        {
          "type": "text",
          "caption": "what you judge",
          "text": "The agent wants to keep this code as it is, for the reason below. The judge agreed. Does the reason justify keeping it?"
        },
        {
          "type": "facts",
          "caption": "positions",
          "facts": [
            {
              "label": "agent",
              "value": "keep",
              "tone": "good"
            },
            {
              "label": "the judge",
              "value": "keep",
              "tone": "good"
            }
          ],
          "agreement": true
        },
        {
          "type": "text",
          "caption": "agent: the reason",
          "text": "ValueConverter is a System.Text.Json JsonConverter, whose contract puts Read and Write in one class. Read uses the read function and Write the write function, so LCOM4 sees two pieces, but they are the two directions of one conversion for one value type, and the serializer takes a converter only with both."
        },
        {
          "type": "text",
          "caption": "judge: why it holds",
          "text": "The reason correctly identifies that the class must implement both methods to satisfy the JsonConverter contract for a single type conversion, justifying why the two pieces are logically coupled."
        },
        {
          "type": "code",
          "caption": "the code now",
          "path": "src/Navarone.Engine/Reasons/ReasonsFile.cs",
          "startLine": 110,
          "code": "    private sealed class ValueConverter<T>(Func<string, T> read, Func<T, string> write) : JsonConverter<T>\n    {\n        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>\n            read(reader.GetString() ?? throw new JsonException($\"{FileName} holds a null where a {typeof(T).Name} belongs.\"));\n\n        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>\n            writer.WriteStringValue(write(value));\n    }",
          "highlights": []
        },
        {
          "type": "facts",
          "caption": "the score",
          "facts": [
            {
              "label": "check",
              "value": "lcom4"
            },
            {
              "label": "value",
              "value": "2"
            },
            {
              "label": "limit",
              "value": "1"
            },
            {
              "label": "over the limit by",
              "value": "1"
            }
          ]
        },
        {
          "type": "timeline",
          "caption": "history",
          "steps": [
            {
              "label": "version 1",
              "when": "2026-10-29T14:47:55.0000000+01:00",
              "blocks": [
                {
                  "type": "facts",
                  "caption": "the version",
                  "facts": [
                    {
                      "label": "from",
                      "value": "git"
                    },
                    {
                      "label": "score",
                      "value": "2"
                    },
                    {
                      "label": "when",
                      "value": "2026-10-29T14:47:55.0000000+01:00"
                    },
                    {
                      "label": "commit",
                      "value": "5bfa844f00c97001775bb0e694d29fbd07a05680"
                    }
                  ]
                },
                {
                  "type": "code",
                  "caption": "the code",
                  "path": "src/Navarone.Engine/Reasons/ReasonsFile.cs",
                  "startLine": 108,
                  "code": "    private sealed class ValueConverter<T>(Func<string, T> read, Func<T, string> write) : JsonConverter<T>\n    {\n        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>\n            read(reader.GetString() ?? throw new JsonException($\"{FileName} holds a null where a {typeof(T).Name} belongs.\"));\n\n        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>\n            writer.WriteStringValue(write(value));\n    }",
                  "highlights": []
                },
                {
                  "type": "text",
                  "caption": "reason: accepted by the judge",
                  "text": "ValueConverter is a System.Text.Json JsonConverter, whose contract puts Read and Write in one class. Read uses the read function and Write the write function, so LCOM4 sees two pieces, but they are the two directions of one conversion for one value type, and the serializer takes a converter only with both.\n\nwhy: The reason correctly identifies that the class must implement both methods to satisfy the JsonConverter contract for a single type conversion, justifying why the two pieces are logically coupled."
                },
                {
                  "type": "facts",
                  "caption": "positions",
                  "facts": [
                    {
                      "label": "agent",
                      "value": "keep",
                      "tone": "good"
                    },
                    {
                      "label": "the judge",
                      "value": "keep",
                      "tone": "good"
                    }
                  ],
                  "agreement": true
                }
              ]
            }
          ]
        }
      ],
      "choices": [
        {
          "id": "accept",
          "label": "Keep: the reason holds"
        },
        {
          "id": "reject",
          "label": "Replace: the reason does not hold"
        }
      ],
      "group": "master",
      "commit": "5354fe779cd2d5ddde6c11192b2de78dfd285f7f",
      "stale": false
    },
    {
      "id": "accepted-8605ada62b8f9aca0a629d0e2849432a",
      "repo": "@REPO@",
      "title": "0000019 split-complexity Navarone.Classifiers::Navarone.Classifiers.Deterministic.DeterministicProblemClassifier.ClassifyAsync(ProblemCase, CancellationToken)",
      "tags": [
        "split-complexity",
        "accepted"
      ],
      "created": "2026-11-01T11:38:00.5661123+00:00",
      "blocks": [
        {
          "type": "text",
          "caption": "what you judge",
          "text": "The agent wants to keep this code as it is, for the reason below. The judge agreed. Does the reason justify keeping it?"
        },
        {
          "type": "facts",
          "caption": "positions",
          "facts": [
            {
              "label": "agent",
              "value": "keep",
              "tone": "good"
            },
            {
              "label": "the judge",
              "value": "keep",
              "tone": "good"
            }
          ],
          "agreement": true
        },
        {
          "type": "text",
          "caption": "agent: the reason",
          "text": "ClassifyAsync only adapts the IProblemClassifier contract to a Task; Classify is one rule per check, and IsValueClass is the value class predicate the lcom4 rule asks, a separate question with its own loop over the class's methods."
        },
        {
          "type": "text",
          "caption": "judge: why it holds",
          "text": "The reason correctly identifies that the split is not arbitrary but follows the logical structure of the code, where Classify contains distinct rules for different checks and IsValueClass is a specialized predicate used by one of those rules."
        },
        {
          "type": "code",
          "caption": "the code now",
          "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
          "startLine": 21,
          "code": "    public Task<ProblemClassification> ClassifyAsync(ProblemCase problem, CancellationToken cancellationToken) =>\n        Task.FromResult(Classify(problem));",
          "highlights": []
        },
        {
          "type": "code",
          "caption": "Classify: adds 8 decisions",
          "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
          "startLine": 24,
          "code": "    private static ProblemClassification Classify(ProblemCase problem)\n    {\n        var finding = problem.Finding;\n        var method = problem.Facts.Types.SelectMany(type => type.Methods).FirstOrDefault(method => method.Symbol == finding.Subject);\n        if (finding.Check is \"cyclomatic\" or \"cognitive\" && method is { Dispatch: not Dispatch.None })\n        {\n            return new(ProblemVerdict.FalsePositive, null, Classifier.Deterministic, \"the method is a flat dispatch: every branch is a single return or expression\");\n        }\n\n        if (finding.Check == \"control-coupling\" && method is { Dispatch: Dispatch.Mapping })\n        {\n            return new(ProblemVerdict.FalsePositive, null, Classifier.Deterministic, \"the method is a mapping: a flat dispatch whose every branch only returns a value\");\n        }\n\n        if (finding.Check == \"lcom4\" && problem.Facts.Types.FirstOrDefault(type => type.Symbol == finding.Subject) is { } type && IsValueClass(type))\n        {\n            return new(ProblemVerdict.FalsePositive, null, Classifier.Deterministic, \"the class is a value class: every method is a property accessor over its own field, so each piece is data, not a separate job\");\n        }\n\n        return new(ProblemVerdict.Undecided, null, Classifier.Deterministic, \"no deterministic rule decides this problem\");\n    }",
          "highlights": [
            28,
            33,
            38
          ]
        },
        {
          "type": "code",
          "caption": "IsValueClass: adds 2 decisions",
          "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
          "startLine": 48,
          "code": "    private static bool IsValueClass(TypeFacts type)\n    {\n        var fields = type.Fields.Where(field => !field.IsStatic).Select(field => field.Symbol).ToHashSet();\n        return type.Methods.Where(method => !method.IsConstructor).All(method =>\n            method is { IsAccessor: true, ControlFlow.Count: 0, Calls.Count: 0 }\n            && method.FieldUses.All(fields.Contains)\n            && method.FieldUses.Count <= 1);\n    }",
          "highlights": [
            52
          ]
        },
        {
          "type": "facts",
          "caption": "the score",
          "facts": [
            {
              "label": "check",
              "value": "split-complexity"
            },
            {
              "label": "value",
              "value": "11"
            },
            {
              "label": "limit",
              "value": "10"
            },
            {
              "label": "over the limit by",
              "value": "1"
            }
          ]
        },
        {
          "type": "timeline",
          "caption": "history",
          "steps": [
            {
              "label": "version 1",
              "when": "2026-10-29T14:47:55.0000000+01:00",
              "blocks": [
                {
                  "type": "facts",
                  "caption": "the version",
                  "facts": [
                    {
                      "label": "from",
                      "value": "git"
                    },
                    {
                      "label": "score",
                      "value": "12"
                    },
                    {
                      "label": "when",
                      "value": "2026-10-29T14:47:55.0000000+01:00"
                    },
                    {
                      "label": "commit",
                      "value": "5bfa844f00c97001775bb0e694d29fbd07a05680"
                    }
                  ]
                },
                {
                  "type": "code",
                  "caption": "the code",
                  "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
                  "startLine": 21,
                  "code": "    public Task<ProblemClassification> ClassifyAsync(ProblemCase problem, CancellationToken cancellationToken) =>\n        Task.FromResult(Classify(problem));",
                  "highlights": []
                },
                {
                  "type": "text",
                  "caption": "reason: for the user, judged an attempt to fool the gate by the judge",
                  "text": "ClassifyAsync only wraps Classify in a completed task for the async contract. Classify holds one if per deterministic rule SPEC gives the problem classifier, split-complexity, flat dispatch, mapping and value class, and IsValueClass is the value class rule for lcom4. The twelve paths are those rules; inlining them would keep every one.\n\nwhy: The reason is a garbled attempt to explain the logic of the classifier itself rather than explaining why the specific code's complexity was split into helpers."
                },
                {
                  "type": "facts",
                  "caption": "positions",
                  "facts": [
                    {
                      "label": "agent",
                      "value": "keep",
                      "tone": "good"
                    },
                    {
                      "label": "the judge",
                      "value": "replace",
                      "tone": "bad"
                    }
                  ],
                  "agreement": true
                }
              ]
            },
            {
              "label": "version 2",
              "when": null,
              "blocks": [
                {
                  "type": "facts",
                  "caption": "the version",
                  "facts": [
                    {
                      "label": "from",
                      "value": "unknown"
                    }
                  ]
                },
                {
                  "type": "text",
                  "caption": "unknown",
                  "text": "a reason answered this code before Navarone saved versions"
                },
                {
                  "type": "text",
                  "caption": "reason: accepted by the judge",
                  "text": "ClassifyAsync only adapts the IProblemClassifier contract to a Task; Classify is one rule per check, and IsValueClass is the value class predicate the lcom4 rule asks, a separate question with its own loop over the class's methods.\n\nwhy: The reason correctly identifies that the split is not arbitrary but follows the logical structure of the code, where Classify contains distinct rules for different checks and IsValueClass is a specialized predicate used by one of those rules."
                },
                {
                  "type": "facts",
                  "caption": "positions",
                  "facts": [
                    {
                      "label": "agent",
                      "value": "keep",
                      "tone": "good"
                    },
                    {
                      "label": "the judge",
                      "value": "keep",
                      "tone": "good"
                    }
                  ],
                  "agreement": true
                }
              ]
            }
          ]
        }
      ],
      "choices": [
        {
          "id": "accept",
          "label": "Keep: the reason holds"
        },
        {
          "id": "reject",
          "label": "Replace: the reason does not hold"
        }
      ],
      "stale": false,
      "group": "self-audit",
      "commit": "2c0e96878c4c1f0e9b7d3a5c8e2f4a6b1d3c5e7f"
    },
    {
      "id": "accepted-4b1e162e2ad713de19e5861cedf4bfc0",
      "repo": "@REPO@",
      "title": "0000043 lcom4 Navarone.Engine::Navarone.Engine.NavaroneJson.ValueConverter<T>",
      "tags": [
        "lcom4",
        "accepted"
      ],
      "created": "2026-11-02T15:28:00.3126068+00:00",
      "blocks": [
        {
          "type": "text",
          "caption": "what you judge",
          "text": "The agent wants to keep this code as it is, for the reason below. The judge agreed. Does the reason justify keeping it?"
        },
        {
          "type": "facts",
          "caption": "positions",
          "facts": [
            {
              "label": "agent",
              "value": "keep",
              "tone": "good"
            },
            {
              "label": "the judge",
              "value": "keep",
              "tone": "good"
            }
          ],
          "agreement": true
        },
        {
          "type": "text",
          "caption": "agent: the reason",
          "text": "ValueConverter is a System.Text.Json JsonConverter, whose contract puts Read and Write in one class. Read uses the read function and Write the write function, so LCOM4 sees two pieces, but they are the two directions of one conversion for one value type, and the serializer takes a converter only with both."
        },
        {
          "type": "text",
          "caption": "judge: why it holds",
          "text": "Held: ValueConverter is a System.Text.Json JsonConverter.; The contract of JsonConverter puts Read and Write in one class.; Read uses the read function.; Write uses the write function.; LCOM4 sees two pieces.; They are the two directions of one conversion for one value type.; The serializer takes a converter only with both.. The reason correctly identifies that the class implements a standard library interface (JsonConverter) which mandates the co-location of Read and Write methods, explaining why the LCOM4 metric flags a false positive for a single cohesive responsibility."
        },
        {
          "type": "claims",
          "caption": "the judge's claims",
          "claims": [
            {
              "text": "ValueConverter is a System.Text.Json JsonConverter.",
              "evidence": "The class declaration `private sealed class ValueConverter<T>... : JsonConverter<T>` shows it inherits from `JsonConverter<T>`.",
              "holds": true
            },
            {
              "text": "The contract of JsonConverter puts Read and Write in one class.",
              "evidence": "This is a known fact of the System.Text.Json library; `JsonConverter<T>` is an abstract class that requires implementations of `Read` and `Write` to function as a converter.",
              "holds": true
            },
            {
              "text": "Read uses the read function.",
              "evidence": "The `Read` method body is `read(reader.GetString() ?? throw new JsonException(...))`, which calls the `read` field (injected via the primary constructor).",
              "holds": true
            },
            {
              "text": "Write uses the write function.",
              "evidence": "The `Write` method body is `writer.WriteStringValue(write(value))`, which calls the `write` field (injected via the primary constructor).",
              "holds": true
            },
            {
              "text": "LCOM4 sees two pieces.",
              "evidence": "The `Read` method only accesses the `read` field. The `Write` method only accesses the `write` field. They do not share any instance fields, resulting in two distinct cohesion groups.",
              "holds": true
            },
            {
              "text": "They are the two directions of one conversion for one value type.",
              "evidence": "The class is generic over `T` and the `read`/`write` functions are both typed to convert between `string` and `T`, representing the bidirectional conversion logic for that specific type.",
              "holds": true
            },
            {
              "text": "The serializer takes a converter only with both.",
              "evidence": "This is a known fact of the System.Text.Json library; a `JsonConverter` must be able to both serialize (Write) and deserialize (Read) to be useful in the JSON pipeline.",
              "holds": true
            }
          ]
        },
        {
          "type": "code",
          "caption": "the code now",
          "path": "src/Navarone.Engine/NavaroneJson.cs",
          "startLine": 49,
          "code": "    private sealed class ValueConverter<T>(Func<string, T> read, Func<T, string> write) : JsonConverter<T>\n    {\n        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>\n            read(reader.GetString() ?? throw new JsonException($\"The JSON holds a null where a {typeof(T).Name} belongs.\"));\n\n        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>\n            writer.WriteStringValue(write(value));\n    }",
          "highlights": []
        },
        {
          "type": "facts",
          "caption": "the score",
          "facts": [
            {
              "label": "check",
              "value": "lcom4"
            },
            {
              "label": "value",
              "value": "2"
            },
            {
              "label": "limit",
              "value": "1"
            },
            {
              "label": "over the limit by",
              "value": "1"
            }
          ]
        },
        {
          "type": "timeline",
          "caption": "history",
          "steps": [
            {
              "label": "version 1",
              "when": "2026-11-02T15:13:03.3087864+00:00",
              "blocks": [
                {
                  "type": "facts",
                  "caption": "the version",
                  "facts": [
                    {
                      "label": "from",
                      "value": "saved"
                    },
                    {
                      "label": "score",
                      "value": "2"
                    },
                    {
                      "label": "when",
                      "value": "2026-11-02T15:13:03.3087864+00:00"
                    }
                  ]
                },
                {
                  "type": "code",
                  "caption": "the code",
                  "path": "src/Navarone.Engine/NavaroneJson.cs",
                  "startLine": 42,
                  "code": "    private sealed class ValueConverter<T>(Func<string, T> read, Func<T, string> write) : JsonConverter<T>\n    {\n        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>\n            read(reader.GetString() ?? throw new JsonException($\"The JSON holds a null where a {typeof(T).Name} belongs.\"));\n\n        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>\n            writer.WriteStringValue(write(value));\n    }",
                  "highlights": []
                },
                {
                  "type": "text",
                  "caption": "reason: rejected by the deterministic classifier",
                  "text": "\n\nwhy: the reason has 0 words; give at least 8"
                },
                {
                  "type": "facts",
                  "caption": "positions",
                  "facts": [
                    {
                      "label": "agent",
                      "value": "keep",
                      "tone": "good"
                    },
                    {
                      "label": "the deterministic classifier",
                      "value": "replace",
                      "tone": "bad"
                    }
                  ],
                  "agreement": true
                },
                {
                  "type": "text",
                  "caption": "reason: accepted by the judge",
                  "text": "ValueConverter is a System.Text.Json JsonConverter, whose contract puts Read and Write in one class. Read uses the read function and Write the write function, so LCOM4 sees two pieces, but they are the two directions of one conversion for one value type, and the serializer takes a converter only with both.\n\nwhy: Held: ValueConverter is a System.Text.Json JsonConverter.; The contract of JsonConverter puts Read and Write in one class.; Read uses the read function.; Write uses the write function.; LCOM4 sees two pieces.; They are the two directions of one conversion for one value type.; The serializer takes a converter only with both.. The reason correctly identifies that the class implements a standard library interface (JsonConverter) which mandates the co-location of Read and Write methods, explaining why the LCOM4 metric flags a false positive for a single cohesive responsibility."
                },
                {
                  "type": "facts",
                  "caption": "positions",
                  "facts": [
                    {
                      "label": "agent",
                      "value": "keep",
                      "tone": "good"
                    },
                    {
                      "label": "the judge",
                      "value": "keep",
                      "tone": "good"
                    }
                  ],
                  "agreement": true
                }
              ]
            }
          ]
        }
      ],
      "choices": [
        {
          "id": "accept",
          "label": "Keep: the reason holds"
        },
        {
          "id": "reject",
          "label": "Replace: the reason does not hold"
        }
      ],
      "stale": false,
      "group": "self-audit",
      "commit": "2c0e96878c4c1f0e9b7d3a5c8e2f4a6b1d3c5e7f"
    }
  ]
}
EOF

questions="${questions//@REPO@/$repo}"
printf '%s\n' "${questions//@SOURCE@/$name}"
