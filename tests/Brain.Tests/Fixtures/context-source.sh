#!/usr/bin/env bash
# A source's context command, as the third column of its line in sources.tsv names one: brain runs it in the
# question's repo with the query's words as arguments of their own, and it prints the page as JSON, shaped as
# `navarone lens --json` prints one. A method query shows the method with a mark on each line that calls methods in
# the codebase, one query per method, so a line of two calls carries two; then a section per view, its results nested
# under it: the class short and the methods an expansion opens, each with its own class section under it, three deep;
# and code shown once, so a second showing is its first line marked with the section above.
# Every mark weighs 0. A query the fixture holds no page for fails as the lens fails, and the first word fail makes it
# fail with two lines on stderr and hang makes it hang. With CONTEXT_PROBE=1 it adds two sections for the tests: the
# directory it ran in, and every word it was given, one fact each, as it received them.
#
#   bash context-source.sh method DeterministicProblemClassifier.Classify class:short expand:1 [class:short]
set -euo pipefail

case "${1:-}" in
    fail)
        echo "lens: no symbol answers the query" >&2
        echo "lens: try method <Type>.<Method>" >&2
        exit 2
        ;;
    hang)
        sleep 60
        ;;
esac

# A word as a JSON string: backslashes, quotes and the control characters a word can hold escaped.
json() {
    local s=$1
    s=${s//\\/\\\\}
    s=${s//\"/\\\"}
    s=${s//$'\t'/\\t}
    s=${s//$'\n'/\\n}
    s=${s//$'\r'/\\r}
    printf '"%s"' "$s"
}

read -r -d '' classify <<'EOF' || true
[
  {
    "id": "s1",
    "title": "method DeterministicProblemClassifier.Classify, overload 1 of 1",
    "open": true,
    "blocks": [
      {
        "type": "code",
        "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
        "startLine": 24,
        "code": "    private static ProblemClassification Classify(ProblemCase problem)\n    {\n        var finding = problem.Finding;\n        var method = Methods.Of(problem.Facts).FirstOrDefault(method => method.Symbol == finding.Subject);\n        if (finding.Check is \"cyclomatic\" or \"cognitive\" && method is { Dispatch: not Dispatch.None })\n        {\n            return new(ProblemVerdict.FalsePositive, null, Classifier.Deterministic, \"the method is a flat dispatch\");\n        }\n\n        if (finding.Check == \"lcom4\" && TypeOf(problem.Facts, finding.Subject) is { } type && IsValueClass(type))\n        {\n            return new(ProblemVerdict.FalsePositive, null, Classifier.Deterministic, \"the class is a value class\");\n        }\n\n        return new(ProblemVerdict.Undecided, null, Classifier.Deterministic, \"no deterministic rule decides this problem\");\n    }",
        "marks": [
          {
            "start": {
              "line": 27,
              "col": 9
            },
            "end": {
              "line": 27,
              "col": 107
            },
            "label": "call",
            "weight": 0,
            "note": "opens a method it calls as the subject",
            "queries": [
              {
                "label": "Methods.Of",
                "query": "method Methods.Of class:short callers:3"
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
              "col": 114
            },
            "label": "call",
            "weight": 0,
            "note": "opens a method it calls as the subject",
            "queries": [
              {
                "label": "DeterministicProblemClassifier.TypeOf",
                "query": "method DeterministicProblemClassifier.TypeOf class:short"
              },
              {
                "label": "DeterministicProblemClassifier.IsValueClass",
                "query": "method DeterministicProblemClassifier.IsValueClass class:short"
              }
            ]
          }
        ]
      }
    ]
  },
  {
    "id": "s2",
    "title": "class:short",
    "open": false,
    "blocks": [],
    "sections": [
      {
        "id": "s3",
        "title": "DeterministicProblemClassifier",
        "open": false,
        "blocks": [
          {
            "type": "code",
            "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
            "startLine": 12,
            "code": "internal sealed class DeterministicProblemClassifier : IProblemClassifier\npublic Task<ProblemVerdict> ClassifyAsync(ProblemCase problem, CancellationToken token)\nprivate static ProblemClassification Classify(ProblemCase problem)\nprivate static TypeFacts? TypeOf(CodebaseFacts facts, SymbolId subject)\nprivate static bool IsValueClass(TypeFacts type)",
            "marks": []
          }
        ]
      },
      {
        "id": "s4",
        "title": "Fields and properties it uses",
        "open": false,
        "blocks": [
          {
            "type": "text",
            "text": "It uses none of DeterministicProblemClassifier's fields or properties."
          }
        ]
      }
    ]
  },
  {
    "id": "s5",
    "title": "expand:1 [class:short]",
    "open": false,
    "blocks": [],
    "sections": [
      {
        "id": "s6",
        "title": "DeterministicProblemClassifier.TypeOf",
        "open": false,
        "blocks": [
          {
            "type": "text",
            "text": "Called by DeterministicProblemClassifier.Classify."
          },
          {
            "type": "code",
            "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
            "startLine": 46,
            "code": "    private static TypeFacts? TypeOf(CodebaseFacts facts, SymbolId subject) =>\n        facts.Types.FirstOrDefault(type => type.Symbol == subject);",
            "marks": []
          }
        ],
        "sections": [
          {
            "id": "s7",
            "title": "class:short",
            "open": false,
            "blocks": [],
            "sections": [
              {
                "id": "s8",
                "title": "DeterministicProblemClassifier",
                "open": false,
                "blocks": [
                  {
                    "type": "code",
                    "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
                    "startLine": 12,
                    "code": "internal sealed class DeterministicProblemClassifier : IProblemClassifier",
                    "marks": [
                      {
                        "start": {
                          "line": 12,
                          "col": 1
                        },
                        "end": {
                          "line": 12,
                          "col": 74
                        },
                        "label": "shown above",
                        "weight": 0,
                        "note": "Shown above at src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs:12.",
                        "section": "s3"
                      }
                    ]
                  }
                ]
              },
              {
                "id": "s9",
                "title": "Fields and properties it uses",
                "open": false,
                "blocks": [
                  {
                    "type": "text",
                    "text": "It uses none of DeterministicProblemClassifier's fields or properties."
                  }
                ]
              }
            ]
          }
        ]
      },
      {
        "id": "s10",
        "title": "DeterministicProblemClassifier.IsValueClass",
        "open": false,
        "blocks": [
          {
            "type": "text",
            "text": "Called by DeterministicProblemClassifier.Classify."
          },
          {
            "type": "code",
            "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
            "startLine": 49,
            "code": "    private static bool IsValueClass(TypeFacts type) =>\n        type.Methods.All(method => method.IsAccessor && method.FieldUses.Count == 1);",
            "marks": []
          }
        ],
        "sections": [
          {
            "id": "s11",
            "title": "class:short",
            "open": false,
            "blocks": [],
            "sections": [
              {
                "id": "s12",
                "title": "DeterministicProblemClassifier",
                "open": false,
                "blocks": [
                  {
                    "type": "code",
                    "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
                    "startLine": 12,
                    "code": "internal sealed class DeterministicProblemClassifier : IProblemClassifier",
                    "marks": [
                      {
                        "start": {
                          "line": 12,
                          "col": 1
                        },
                        "end": {
                          "line": 12,
                          "col": 74
                        },
                        "label": "shown above",
                        "weight": 0,
                        "note": "Shown above at src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs:12.",
                        "section": "s3"
                      }
                    ]
                  }
                ]
              },
              {
                "id": "s13",
                "title": "Fields and properties it uses",
                "open": false,
                "blocks": [
                  {
                    "type": "text",
                    "text": "It uses none of DeterministicProblemClassifier's fields or properties."
                  }
                ]
              }
            ]
          }
        ]
      }
    ]
  }
]
EOF

read -r -d '' isvalue <<'EOF' || true
[
  {
    "id": "s1",
    "title": "method DeterministicProblemClassifier.IsValueClass, overload 1 of 1",
    "open": true,
    "blocks": [
      {
        "type": "code",
        "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
        "startLine": 49,
        "code": "    private static bool IsValueClass(TypeFacts type) =>\n        type.Methods.All(method => method.IsAccessor && method.FieldUses.Count == 1);",
        "marks": []
      }
    ]
  },
  {
    "id": "s2",
    "title": "class:short",
    "open": false,
    "blocks": []
  },
  {
    "id": "s3",
    "title": "DeterministicProblemClassifier",
    "open": false,
    "blocks": [
      {
        "type": "code",
        "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
        "startLine": 12,
        "code": "internal sealed class DeterministicProblemClassifier : IProblemClassifier\npublic Task<ProblemVerdict> ClassifyAsync(ProblemCase problem, CancellationToken token)\nprivate static ProblemClassification Classify(ProblemCase problem)\nprivate static TypeFacts? TypeOf(CodebaseFacts facts, SymbolId subject)\nprivate static bool IsValueClass(TypeFacts type)",
        "marks": []
      }
    ]
  },
  {
    "id": "s4",
    "title": "Fields and properties it uses",
    "open": false,
    "blocks": [
      {
        "type": "text",
        "text": "It uses none of DeterministicProblemClassifier's fields or properties."
      }
    ]
  }
]
EOF

read -r -d '' typeof <<'EOF' || true
[
  {
    "id": "s1",
    "title": "method DeterministicProblemClassifier.TypeOf, overload 1 of 1",
    "open": true,
    "blocks": [
      {
        "type": "code",
        "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
        "startLine": 46,
        "code": "    private static TypeFacts? TypeOf(CodebaseFacts facts, SymbolId subject) =>\n        facts.Types.FirstOrDefault(type => type.Symbol == subject);",
        "marks": []
      }
    ]
  },
  {
    "id": "s2",
    "title": "class:short",
    "open": false,
    "blocks": []
  },
  {
    "id": "s3",
    "title": "DeterministicProblemClassifier",
    "open": false,
    "blocks": [
      {
        "type": "code",
        "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
        "startLine": 12,
        "code": "internal sealed class DeterministicProblemClassifier : IProblemClassifier\npublic Task<ProblemVerdict> ClassifyAsync(ProblemCase problem, CancellationToken token)\nprivate static ProblemClassification Classify(ProblemCase problem)\nprivate static TypeFacts? TypeOf(CodebaseFacts facts, SymbolId subject)\nprivate static bool IsValueClass(TypeFacts type)",
        "marks": []
      }
    ]
  },
  {
    "id": "s4",
    "title": "Fields and properties it uses",
    "open": false,
    "blocks": [
      {
        "type": "text",
        "text": "It uses none of DeterministicProblemClassifier's fields or properties."
      }
    ]
  }
]
EOF

read -r -d '' methodsof <<'EOF' || true
[
  {
    "id": "s1",
    "title": "method Methods.Of, overload 1 of 1",
    "open": true,
    "blocks": [
      {
        "type": "code",
        "path": "src/Navarone.Engine/Facts/Methods.cs",
        "startLine": 18,
        "code": "    public static IEnumerable<MethodFacts> Of(CodebaseFacts facts) =>\n        facts.Types.SelectMany(type => type.Methods);",
        "marks": []
      }
    ]
  },
  {
    "id": "s2",
    "title": "class:short",
    "open": false,
    "blocks": []
  },
  {
    "id": "s3",
    "title": "Methods",
    "open": false,
    "blocks": [
      {
        "type": "code",
        "path": "src/Navarone.Engine/Facts/Methods.cs",
        "startLine": 9,
        "code": "internal static class Methods\npublic static IEnumerable<MethodFacts> Of(CodebaseFacts facts)",
        "marks": []
      }
    ]
  },
  {
    "id": "s4",
    "title": "Fields and properties it uses",
    "open": false,
    "blocks": [
      {
        "type": "text",
        "text": "It uses none of Methods's fields or properties."
      }
    ]
  },
  {
    "id": "s5",
    "title": "callers:3",
    "open": false,
    "blocks": []
  },
  {
    "id": "s6",
    "title": "DeterministicProblemClassifier.Classify",
    "open": false,
    "blocks": [
      {
        "type": "text",
        "text": "Calls it at `src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs:27`."
      },
      {
        "type": "code",
        "path": "src/Navarone.Classifiers/Deterministic/DeterministicProblemClassifier.cs",
        "startLine": 26,
        "code": "        var finding = problem.Finding;\n        var method = Methods.Of(problem.Facts).FirstOrDefault(method => method.Symbol == finding.Subject);\n        if (finding.Check is \"cyclomatic\" or \"cognitive\" && method is { Dispatch: not Dispatch.None })",
        "marks": [
          {
            "start": {
              "line": 27,
              "col": 9
            },
            "end": {
              "line": 27,
              "col": 107
            },
            "label": "caller",
            "weight": 0,
            "note": "opens the calling method, or a method this line calls, as the subject",
            "queries": [
              {
                "label": "DeterministicProblemClassifier.Classify",
                "query": "method DeterministicProblemClassifier.Classify class:short"
              },
              {
                "label": "Methods.Of",
                "query": "method Methods.Of class:short callers:3"
              }
            ]
          }
        ]
      }
    ]
  }
]
EOF

read -r -d '' runasync <<'EOF' || true
[
  {
    "id": "s1",
    "title": "method Commands.RunAsync, overload 1 of 1",
    "open": true,
    "blocks": [
      {
        "type": "code",
        "path": "src/Navarone.Cli/Commands.cs",
        "startLine": 30,
        "code": "    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)\n    {\n        if (Parse(args) is not { } command)\n        {\n            return await Usage(error).ConfigureAwait(false);\n        }\n\n        return await command.RunAsync(output, error).ConfigureAwait(false);\n    }",
        "marks": [
          {
            "start": {
              "line": 32,
              "col": 9
            },
            "end": {
              "line": 32,
              "col": 44
            },
            "label": "call",
            "weight": 0,
            "note": "opens a method it calls as the subject",
            "queries": [
              {
                "label": "Commands.Parse",
                "query": "method Commands.Parse class:short"
              }
            ]
          },
          {
            "start": {
              "line": 34,
              "col": 13
            },
            "end": {
              "line": 34,
              "col": 61
            },
            "label": "call",
            "weight": 0,
            "note": "opens a method it calls as the subject",
            "queries": [
              {
                "label": "Commands.Usage",
                "query": "method Commands.Usage class:short"
              }
            ]
          }
        ]
      }
    ]
  },
  {
    "id": "s2",
    "title": "class:short",
    "open": false,
    "blocks": []
  },
  {
    "id": "s3",
    "title": "Commands",
    "open": false,
    "blocks": [
      {
        "type": "code",
        "path": "src/Navarone.Cli/Commands.cs",
        "startLine": 12,
        "code": "internal static class Commands\npublic static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)\nprivate static Command? Parse(string[] args)\nprivate static Task<int> Usage(TextWriter error)",
        "marks": []
      }
    ]
  },
  {
    "id": "s4",
    "title": "Fields and properties it uses",
    "open": false,
    "blocks": [
      {
        "type": "text",
        "text": "It uses none of Commands's fields or properties."
      }
    ]
  },
  {
    "id": "s5",
    "title": "callers:3",
    "open": false,
    "blocks": []
  },
  {
    "id": "s6",
    "title": "Program.Main",
    "open": false,
    "blocks": [
      {
        "type": "text",
        "text": "Calls it at `src/Navarone.Cli/Program.cs:9`."
      },
      {
        "type": "code",
        "path": "src/Navarone.Cli/Program.cs",
        "startLine": 8,
        "code": "    {\n        return await Commands.RunAsync(args, Console.Out, Console.Error).ConfigureAwait(false);\n    }",
        "marks": [
          {
            "start": {
              "line": 9,
              "col": 9
            },
            "end": {
              "line": 9,
              "col": 96
            },
            "label": "caller",
            "weight": 0,
            "note": "opens the calling method, or a method this line calls, as the subject",
            "queries": [
              {
                "label": "Program.Main",
                "query": "method Program.Main class:short callers:3"
              },
              {
                "label": "Commands.RunAsync",
                "query": "method Commands.RunAsync class:short callers:3"
              }
            ]
          }
        ]
      }
    ]
  }
]
EOF

case "${1:-} ${2:-}" in
    "method DeterministicProblemClassifier.Classify") sections=$classify ;;
    "method DeterministicProblemClassifier.IsValueClass") sections=$isvalue ;;
    "method DeterministicProblemClassifier.TypeOf") sections=$typeof ;;
    "method Methods.Of") sections=$methodsof ;;
    "method Commands.RunAsync") sections=$runasync ;;
    *)
        echo "lens: no method ${2:-} in the codebase" >&2
        exit 1
        ;;
esac

if [[ "${CONTEXT_PROBE:-}" == 1 ]]; then
    facts=""
    n=0
    for word in "$@"; do
        n=$((n + 1))
        facts+="${facts:+, }{\"label\": \"word $n\", \"value\": $(json "$word")}"
    done
    where='{"id": "where", "title": "where it ran", "open": false, "blocks": [{"type": "text", "text": '"$(json "$PWD")"'}]}'
    words='{"id": "words", "title": "the query, word by word", "open": false, "blocks": [{"type": "facts", "facts": ['"$facts"']}]}'
    sections="${sections%]}, $where, $words]"
fi

printf '{"commit": "2c0e96878c4c1f0e9b7d3a5c8e2f4a6b1d3c5e7f", "sections": %s}\n' "$sections"
