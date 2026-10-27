// Generated from the real engine (orken-tax-engine/1.0.0) on the seeded catalogue: TaxSimulator.Compare of a
// regular-regime 2026 sale (line 1 fully informed; line 2 with taxes in the price unknown and under a special regime)
// against 'conditional discounts made unconditional'. Kept as data so the screens are tested against what the API returns.
import type { ScenarioResult } from "../api/tax.api";

export const ENGINE_SCENARIOS: ScenarioResult[] = [
  {
    "name": "Cenário atual",
    "isBaseline": true,
    "input": {
      "referenceDate": "2026-06-15",
      "profile": {
        "profileVersionId": "11111111-1111-4111-8111-111111111111",
        "regime": "LucroPresumido",
        "state": "SP",
        "cityIbgeCode": "3550308",
        "accessoryObligationsCompliant": null
      },
      "direction": "Outbound",
      "counterpartyRegime": null,
      "lines": [
        {
          "index": 1,
          "kind": "Good",
          "gross": 1000,
          "unconditionalDiscount": 100,
          "conditionalDiscount": 50,
          "surcharges": 20,
          "excludedTaxes": 80,
          "excludedTaxesInformed": true,
          "specialTreatment": null
        },
        {
          "index": 2,
          "kind": "Good",
          "gross": 1000,
          "unconditionalDiscount": 100,
          "conditionalDiscount": 50,
          "surcharges": 20,
          "excludedTaxes": 0,
          "excludedTaxesInformed": false,
          "specialTreatment": "reducao-60"
        }
      ]
    },
    "output": {
      "engineVersion": "orken-tax-engine/1.0.0",
      "lines": [
        {
          "index": 1,
          "gross": 1000,
          "base": 840,
          "baseSteps": [
            {
              "ruleKey": "IBSCBS.BASE.SURCHARGES",
              "ruleVersion": 1,
              "component": "Surcharges",
              "included": true,
              "delta": 20
            },
            {
              "ruleKey": "IBSCBS.BASE.CONDITIONAL-DISCOUNT",
              "ruleVersion": 1,
              "component": "ConditionalDiscount",
              "included": true,
              "delta": 0
            },
            {
              "ruleKey": "IBSCBS.BASE.UNCONDITIONAL-DISCOUNT",
              "ruleVersion": 1,
              "component": "UnconditionalDiscount",
              "included": false,
              "delta": -100
            },
            {
              "ruleKey": "IBSCBS.BASE.TRANSITION-TAXES",
              "ruleVersion": 1,
              "component": "ExcludedTaxes",
              "included": false,
              "delta": -80
            }
          ],
          "components": [
            {
              "tax": "Cbs",
              "base": 840,
              "ratePercent": 0.9,
              "amount": 7.56,
              "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000005",
              "ruleKey": "CBS.RATE.2026-TEST",
              "isHypothesis": false
            },
            {
              "tax": "IbsState",
              "base": 840,
              "ratePercent": 0.1,
              "amount": 0.84,
              "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000006",
              "ruleKey": "IBS.RATE.2026-TEST-STATE",
              "isHypothesis": false
            }
          ]
        },
        {
          "index": 2,
          "gross": 1000,
          "base": 920,
          "baseSteps": [
            {
              "ruleKey": "IBSCBS.BASE.SURCHARGES",
              "ruleVersion": 1,
              "component": "Surcharges",
              "included": true,
              "delta": 20
            },
            {
              "ruleKey": "IBSCBS.BASE.CONDITIONAL-DISCOUNT",
              "ruleVersion": 1,
              "component": "ConditionalDiscount",
              "included": true,
              "delta": 0
            },
            {
              "ruleKey": "IBSCBS.BASE.UNCONDITIONAL-DISCOUNT",
              "ruleVersion": 1,
              "component": "UnconditionalDiscount",
              "included": false,
              "delta": -100
            },
            {
              "ruleKey": "IBSCBS.BASE.TRANSITION-TAXES",
              "ruleVersion": 1,
              "component": "ExcludedTaxes",
              "included": false,
              "delta": 0
            }
          ],
          "components": []
        }
      ],
      "totals": [
        {
          "tax": "Cbs",
          "base": 840,
          "amount": 7.56
        },
        {
          "tax": "IbsState",
          "base": 840,
          "amount": 0.84
        }
      ],
      "totalBase": 1760,
      "totalTax": 8.4,
      "notes": [
        {
          "code": "IBSCBS-2026-COMPENSATION",
          "text": "O IBS e a CBS recolhidos em 2026 são compensados com o PIS e a Cofins devidos no mesmo período; sem débitos suficientes, compensados com outro tributo federal ou ressarcidos em até 60 dias (LC 214/2025, art. 348, I e II).",
          "ruleKey": "IBSCBS.NOTE.2026-COMPENSATION",
          "legalSourceCodes": [
            "LC214-2025-ART348"
          ]
        }
      ],
      "warnings": [
        "Linha 2: ICMS, ISS, PIS e Cofins embutidos no preço não foram informados — a base pode estar maior do que a real.",
        "Linha 2: item em regime diferenciado ou específico (reducao-60) — esta versão da engine não calcula alíquotas reduzidas nem bases específicas.",
        "A regra IBSCBS.NOTE.2026-WAIVER não pôde ser avaliada: accessoryObligationsCompliant não declarado no perfil."
      ],
      "executions": [
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000001",
          "ruleKey": "IBSCBS.BASE.SURCHARGES",
          "ruleVersion": 1,
          "contentHash": "a876b32b8467f4079ffd8bd892522c89c2415909660b43a326f45c7b157fbf48",
          "lineIndex": 1,
          "matched": true,
          "conditions": [],
          "output": "mantém Surcharges: 20.00",
          "legalSourceCodes": [
            "LC214-2025-ART12-CAPUT-P1"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000002",
          "ruleKey": "IBSCBS.BASE.CONDITIONAL-DISCOUNT",
          "ruleVersion": 1,
          "contentHash": "6ceb5f96f39abf737b5feccb3f516fefc53fc1de45dcd6db7819ebe30a22b47c",
          "lineIndex": 1,
          "matched": true,
          "conditions": [],
          "output": "mantém ConditionalDiscount: 0.00",
          "legalSourceCodes": [
            "LC214-2025-ART12-CAPUT-P1"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000003",
          "ruleKey": "IBSCBS.BASE.UNCONDITIONAL-DISCOUNT",
          "ruleVersion": 1,
          "contentHash": "fc56d1469478465d8b7f0bd07029f375c2ba3d64596930ba0baf3aa342ab380b",
          "lineIndex": 1,
          "matched": true,
          "conditions": [],
          "output": "exclui UnconditionalDiscount: -100.00",
          "legalSourceCodes": [
            "LC214-2025-ART12-P2-III-P3"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000004",
          "ruleKey": "IBSCBS.BASE.TRANSITION-TAXES",
          "ruleVersion": 1,
          "contentHash": "8fe5f049b57aab0c71b0bf9bc466262a6ea4b23418e1e160e501e0d92b22ff93",
          "lineIndex": 1,
          "matched": true,
          "conditions": [],
          "output": "exclui ExcludedTaxes: -80.00",
          "legalSourceCodes": [
            "LC214-2025-ART12-P2-V"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000005",
          "ruleKey": "CBS.RATE.2026-TEST",
          "ruleVersion": 1,
          "contentHash": "e2de5c9cde13f0cb9e4f1c8820d84e2401e08bd05ea727cb9994677252fa09c4",
          "lineIndex": 1,
          "matched": true,
          "conditions": [
            {
              "kind": "supplier-regime-not-in",
              "expected": "SimplesNacional,SimplesNacionalExcessoSublimite,Mei",
              "actual": "LucroPresumido",
              "outcome": "Passed"
            },
            {
              "kind": "item-general-regime",
              "expected": "regime geral",
              "actual": "regime geral",
              "outcome": "Passed"
            }
          ],
          "output": "Cbs 0.9% sobre 840.00 = 7.56",
          "legalSourceCodes": [
            "LC214-2025-ART346",
            "LC214-2025-ART348"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000006",
          "ruleKey": "IBS.RATE.2026-TEST-STATE",
          "ruleVersion": 1,
          "contentHash": "acfeedf5b258de43d760e27a6e9f945b0f8ff541678ced46e1309dbd23e7e76a",
          "lineIndex": 1,
          "matched": true,
          "conditions": [
            {
              "kind": "supplier-regime-not-in",
              "expected": "SimplesNacional,SimplesNacionalExcessoSublimite,Mei",
              "actual": "LucroPresumido",
              "outcome": "Passed"
            },
            {
              "kind": "item-general-regime",
              "expected": "regime geral",
              "actual": "regime geral",
              "outcome": "Passed"
            }
          ],
          "output": "IbsState 0.1% sobre 840.00 = 0.84",
          "legalSourceCodes": [
            "LC214-2025-ART343",
            "LC214-2025-ART348"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000001",
          "ruleKey": "IBSCBS.BASE.SURCHARGES",
          "ruleVersion": 1,
          "contentHash": "a876b32b8467f4079ffd8bd892522c89c2415909660b43a326f45c7b157fbf48",
          "lineIndex": 2,
          "matched": true,
          "conditions": [],
          "output": "mantém Surcharges: 20.00",
          "legalSourceCodes": [
            "LC214-2025-ART12-CAPUT-P1"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000002",
          "ruleKey": "IBSCBS.BASE.CONDITIONAL-DISCOUNT",
          "ruleVersion": 1,
          "contentHash": "6ceb5f96f39abf737b5feccb3f516fefc53fc1de45dcd6db7819ebe30a22b47c",
          "lineIndex": 2,
          "matched": true,
          "conditions": [],
          "output": "mantém ConditionalDiscount: 0.00",
          "legalSourceCodes": [
            "LC214-2025-ART12-CAPUT-P1"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000003",
          "ruleKey": "IBSCBS.BASE.UNCONDITIONAL-DISCOUNT",
          "ruleVersion": 1,
          "contentHash": "fc56d1469478465d8b7f0bd07029f375c2ba3d64596930ba0baf3aa342ab380b",
          "lineIndex": 2,
          "matched": true,
          "conditions": [],
          "output": "exclui UnconditionalDiscount: -100.00",
          "legalSourceCodes": [
            "LC214-2025-ART12-P2-III-P3"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000004",
          "ruleKey": "IBSCBS.BASE.TRANSITION-TAXES",
          "ruleVersion": 1,
          "contentHash": "8fe5f049b57aab0c71b0bf9bc466262a6ea4b23418e1e160e501e0d92b22ff93",
          "lineIndex": 2,
          "matched": true,
          "conditions": [],
          "output": "exclui ExcludedTaxes: 0.00",
          "legalSourceCodes": [
            "LC214-2025-ART12-P2-V"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000005",
          "ruleKey": "CBS.RATE.2026-TEST",
          "ruleVersion": 1,
          "contentHash": "e2de5c9cde13f0cb9e4f1c8820d84e2401e08bd05ea727cb9994677252fa09c4",
          "lineIndex": 2,
          "matched": false,
          "conditions": [
            {
              "kind": "supplier-regime-not-in",
              "expected": "SimplesNacional,SimplesNacionalExcessoSublimite,Mei",
              "actual": "LucroPresumido",
              "outcome": "Passed"
            },
            {
              "kind": "item-general-regime",
              "expected": "regime geral",
              "actual": "reducao-60",
              "outcome": "Failed"
            }
          ],
          "output": "não se aplica",
          "legalSourceCodes": [
            "LC214-2025-ART346",
            "LC214-2025-ART348"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000006",
          "ruleKey": "IBS.RATE.2026-TEST-STATE",
          "ruleVersion": 1,
          "contentHash": "acfeedf5b258de43d760e27a6e9f945b0f8ff541678ced46e1309dbd23e7e76a",
          "lineIndex": 2,
          "matched": false,
          "conditions": [
            {
              "kind": "supplier-regime-not-in",
              "expected": "SimplesNacional,SimplesNacionalExcessoSublimite,Mei",
              "actual": "LucroPresumido",
              "outcome": "Passed"
            },
            {
              "kind": "item-general-regime",
              "expected": "regime geral",
              "actual": "reducao-60",
              "outcome": "Failed"
            }
          ],
          "output": "não se aplica",
          "legalSourceCodes": [
            "LC214-2025-ART343",
            "LC214-2025-ART348"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000007",
          "ruleKey": "IBSCBS.NOTE.2026-SIMPLES",
          "ruleVersion": 1,
          "contentHash": "32489294dadefad3bb8bd6f58c1ee2cd272d8c43f3d1d6d95edb36bd7c39a776",
          "lineIndex": null,
          "matched": false,
          "conditions": [
            {
              "kind": "supplier-regime-in",
              "expected": "SimplesNacional,SimplesNacionalExcessoSublimite,Mei",
              "actual": "LucroPresumido",
              "outcome": "Failed"
            }
          ],
          "output": "não se aplica",
          "legalSourceCodes": [
            "LC214-2025-ART348"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000008",
          "ruleKey": "IBSCBS.NOTE.2026-COMPENSATION",
          "ruleVersion": 1,
          "contentHash": "d6130ea8c549084ba1e5cd517915b05c603564f96a2363057634a2c01034755b",
          "lineIndex": null,
          "matched": true,
          "conditions": [
            {
              "kind": "direction-is",
              "expected": "Outbound",
              "actual": "Outbound",
              "outcome": "Passed"
            },
            {
              "kind": "supplier-regime-not-in",
              "expected": "SimplesNacional,SimplesNacionalExcessoSublimite,Mei",
              "actual": "LucroPresumido",
              "outcome": "Passed"
            }
          ],
          "output": "nota IBSCBS-2026-COMPENSATION",
          "legalSourceCodes": [
            "LC214-2025-ART348"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000009",
          "ruleKey": "IBSCBS.NOTE.2026-WAIVER",
          "ruleVersion": 1,
          "contentHash": "de2ce6c335fa29976a102f8a07f35c28700195947a4ae8ad1565918f6909f294",
          "lineIndex": null,
          "matched": false,
          "conditions": [
            {
              "kind": "direction-is",
              "expected": "Outbound",
              "actual": "Outbound",
              "outcome": "Passed"
            },
            {
              "kind": "supplier-regime-not-in",
              "expected": "SimplesNacional,SimplesNacionalExcessoSublimite,Mei",
              "actual": "LucroPresumido",
              "outcome": "Passed"
            },
            {
              "kind": "profile-flag-is",
              "expected": "accessoryObligationsCompliant=True",
              "actual": "accessoryObligationsCompliant não declarado no perfil",
              "outcome": "Unknown"
            }
          ],
          "output": "não se aplica",
          "legalSourceCodes": [
            "LC214-2025-ART348"
          ],
          "isHypothesis": false
        }
      ],
      "notApplied": []
    },
    "charged": 1740,
    "taxTotal": 8.4,
    "creditEstimate": 0,
    "effectiveCost": 8.4,
    "taxDeltaVsBaseline": 0,
    "effectiveCostDeltaVsBaseline": 0,
    "noCashEffectInPeriod": true,
    "hypotheses": []
  },
  {
    "name": "Desconto incondicional no documento fiscal",
    "isBaseline": false,
    "input": {
      "referenceDate": "2026-06-15",
      "profile": {
        "profileVersionId": "11111111-1111-4111-8111-111111111111",
        "regime": "LucroPresumido",
        "state": "SP",
        "cityIbgeCode": "3550308",
        "accessoryObligationsCompliant": null
      },
      "direction": "Outbound",
      "counterpartyRegime": null,
      "lines": [
        {
          "index": 1,
          "kind": "Good",
          "gross": 1000,
          "unconditionalDiscount": 150,
          "conditionalDiscount": 0,
          "surcharges": 20,
          "excludedTaxes": 80,
          "excludedTaxesInformed": true,
          "specialTreatment": null
        },
        {
          "index": 2,
          "kind": "Good",
          "gross": 1000,
          "unconditionalDiscount": 150,
          "conditionalDiscount": 0,
          "surcharges": 20,
          "excludedTaxes": 0,
          "excludedTaxesInformed": false,
          "specialTreatment": "reducao-60"
        }
      ]
    },
    "output": {
      "engineVersion": "orken-tax-engine/1.0.0",
      "lines": [
        {
          "index": 1,
          "gross": 1000,
          "base": 790,
          "baseSteps": [
            {
              "ruleKey": "IBSCBS.BASE.SURCHARGES",
              "ruleVersion": 1,
              "component": "Surcharges",
              "included": true,
              "delta": 20
            },
            {
              "ruleKey": "IBSCBS.BASE.CONDITIONAL-DISCOUNT",
              "ruleVersion": 1,
              "component": "ConditionalDiscount",
              "included": true,
              "delta": 0
            },
            {
              "ruleKey": "IBSCBS.BASE.UNCONDITIONAL-DISCOUNT",
              "ruleVersion": 1,
              "component": "UnconditionalDiscount",
              "included": false,
              "delta": -150
            },
            {
              "ruleKey": "IBSCBS.BASE.TRANSITION-TAXES",
              "ruleVersion": 1,
              "component": "ExcludedTaxes",
              "included": false,
              "delta": -80
            }
          ],
          "components": [
            {
              "tax": "Cbs",
              "base": 790,
              "ratePercent": 0.9,
              "amount": 7.11,
              "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000005",
              "ruleKey": "CBS.RATE.2026-TEST",
              "isHypothesis": false
            },
            {
              "tax": "IbsState",
              "base": 790,
              "ratePercent": 0.1,
              "amount": 0.79,
              "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000006",
              "ruleKey": "IBS.RATE.2026-TEST-STATE",
              "isHypothesis": false
            }
          ]
        },
        {
          "index": 2,
          "gross": 1000,
          "base": 870,
          "baseSteps": [
            {
              "ruleKey": "IBSCBS.BASE.SURCHARGES",
              "ruleVersion": 1,
              "component": "Surcharges",
              "included": true,
              "delta": 20
            },
            {
              "ruleKey": "IBSCBS.BASE.CONDITIONAL-DISCOUNT",
              "ruleVersion": 1,
              "component": "ConditionalDiscount",
              "included": true,
              "delta": 0
            },
            {
              "ruleKey": "IBSCBS.BASE.UNCONDITIONAL-DISCOUNT",
              "ruleVersion": 1,
              "component": "UnconditionalDiscount",
              "included": false,
              "delta": -150
            },
            {
              "ruleKey": "IBSCBS.BASE.TRANSITION-TAXES",
              "ruleVersion": 1,
              "component": "ExcludedTaxes",
              "included": false,
              "delta": 0
            }
          ],
          "components": []
        }
      ],
      "totals": [
        {
          "tax": "Cbs",
          "base": 790,
          "amount": 7.11
        },
        {
          "tax": "IbsState",
          "base": 790,
          "amount": 0.79
        }
      ],
      "totalBase": 1660,
      "totalTax": 7.9,
      "notes": [
        {
          "code": "IBSCBS-2026-COMPENSATION",
          "text": "O IBS e a CBS recolhidos em 2026 são compensados com o PIS e a Cofins devidos no mesmo período; sem débitos suficientes, compensados com outro tributo federal ou ressarcidos em até 60 dias (LC 214/2025, art. 348, I e II).",
          "ruleKey": "IBSCBS.NOTE.2026-COMPENSATION",
          "legalSourceCodes": [
            "LC214-2025-ART348"
          ]
        }
      ],
      "warnings": [
        "Linha 2: ICMS, ISS, PIS e Cofins embutidos no preço não foram informados — a base pode estar maior do que a real.",
        "Linha 2: item em regime diferenciado ou específico (reducao-60) — esta versão da engine não calcula alíquotas reduzidas nem bases específicas.",
        "A regra IBSCBS.NOTE.2026-WAIVER não pôde ser avaliada: accessoryObligationsCompliant não declarado no perfil."
      ],
      "executions": [
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000001",
          "ruleKey": "IBSCBS.BASE.SURCHARGES",
          "ruleVersion": 1,
          "contentHash": "a876b32b8467f4079ffd8bd892522c89c2415909660b43a326f45c7b157fbf48",
          "lineIndex": 1,
          "matched": true,
          "conditions": [],
          "output": "mantém Surcharges: 20.00",
          "legalSourceCodes": [
            "LC214-2025-ART12-CAPUT-P1"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000002",
          "ruleKey": "IBSCBS.BASE.CONDITIONAL-DISCOUNT",
          "ruleVersion": 1,
          "contentHash": "6ceb5f96f39abf737b5feccb3f516fefc53fc1de45dcd6db7819ebe30a22b47c",
          "lineIndex": 1,
          "matched": true,
          "conditions": [],
          "output": "mantém ConditionalDiscount: 0.00",
          "legalSourceCodes": [
            "LC214-2025-ART12-CAPUT-P1"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000003",
          "ruleKey": "IBSCBS.BASE.UNCONDITIONAL-DISCOUNT",
          "ruleVersion": 1,
          "contentHash": "fc56d1469478465d8b7f0bd07029f375c2ba3d64596930ba0baf3aa342ab380b",
          "lineIndex": 1,
          "matched": true,
          "conditions": [],
          "output": "exclui UnconditionalDiscount: -150.00",
          "legalSourceCodes": [
            "LC214-2025-ART12-P2-III-P3"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000004",
          "ruleKey": "IBSCBS.BASE.TRANSITION-TAXES",
          "ruleVersion": 1,
          "contentHash": "8fe5f049b57aab0c71b0bf9bc466262a6ea4b23418e1e160e501e0d92b22ff93",
          "lineIndex": 1,
          "matched": true,
          "conditions": [],
          "output": "exclui ExcludedTaxes: -80.00",
          "legalSourceCodes": [
            "LC214-2025-ART12-P2-V"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000005",
          "ruleKey": "CBS.RATE.2026-TEST",
          "ruleVersion": 1,
          "contentHash": "e2de5c9cde13f0cb9e4f1c8820d84e2401e08bd05ea727cb9994677252fa09c4",
          "lineIndex": 1,
          "matched": true,
          "conditions": [
            {
              "kind": "supplier-regime-not-in",
              "expected": "SimplesNacional,SimplesNacionalExcessoSublimite,Mei",
              "actual": "LucroPresumido",
              "outcome": "Passed"
            },
            {
              "kind": "item-general-regime",
              "expected": "regime geral",
              "actual": "regime geral",
              "outcome": "Passed"
            }
          ],
          "output": "Cbs 0.9% sobre 790.00 = 7.11",
          "legalSourceCodes": [
            "LC214-2025-ART346",
            "LC214-2025-ART348"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000006",
          "ruleKey": "IBS.RATE.2026-TEST-STATE",
          "ruleVersion": 1,
          "contentHash": "acfeedf5b258de43d760e27a6e9f945b0f8ff541678ced46e1309dbd23e7e76a",
          "lineIndex": 1,
          "matched": true,
          "conditions": [
            {
              "kind": "supplier-regime-not-in",
              "expected": "SimplesNacional,SimplesNacionalExcessoSublimite,Mei",
              "actual": "LucroPresumido",
              "outcome": "Passed"
            },
            {
              "kind": "item-general-regime",
              "expected": "regime geral",
              "actual": "regime geral",
              "outcome": "Passed"
            }
          ],
          "output": "IbsState 0.1% sobre 790.00 = 0.79",
          "legalSourceCodes": [
            "LC214-2025-ART343",
            "LC214-2025-ART348"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000001",
          "ruleKey": "IBSCBS.BASE.SURCHARGES",
          "ruleVersion": 1,
          "contentHash": "a876b32b8467f4079ffd8bd892522c89c2415909660b43a326f45c7b157fbf48",
          "lineIndex": 2,
          "matched": true,
          "conditions": [],
          "output": "mantém Surcharges: 20.00",
          "legalSourceCodes": [
            "LC214-2025-ART12-CAPUT-P1"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000002",
          "ruleKey": "IBSCBS.BASE.CONDITIONAL-DISCOUNT",
          "ruleVersion": 1,
          "contentHash": "6ceb5f96f39abf737b5feccb3f516fefc53fc1de45dcd6db7819ebe30a22b47c",
          "lineIndex": 2,
          "matched": true,
          "conditions": [],
          "output": "mantém ConditionalDiscount: 0.00",
          "legalSourceCodes": [
            "LC214-2025-ART12-CAPUT-P1"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000003",
          "ruleKey": "IBSCBS.BASE.UNCONDITIONAL-DISCOUNT",
          "ruleVersion": 1,
          "contentHash": "fc56d1469478465d8b7f0bd07029f375c2ba3d64596930ba0baf3aa342ab380b",
          "lineIndex": 2,
          "matched": true,
          "conditions": [],
          "output": "exclui UnconditionalDiscount: -150.00",
          "legalSourceCodes": [
            "LC214-2025-ART12-P2-III-P3"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000004",
          "ruleKey": "IBSCBS.BASE.TRANSITION-TAXES",
          "ruleVersion": 1,
          "contentHash": "8fe5f049b57aab0c71b0bf9bc466262a6ea4b23418e1e160e501e0d92b22ff93",
          "lineIndex": 2,
          "matched": true,
          "conditions": [],
          "output": "exclui ExcludedTaxes: 0.00",
          "legalSourceCodes": [
            "LC214-2025-ART12-P2-V"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000005",
          "ruleKey": "CBS.RATE.2026-TEST",
          "ruleVersion": 1,
          "contentHash": "e2de5c9cde13f0cb9e4f1c8820d84e2401e08bd05ea727cb9994677252fa09c4",
          "lineIndex": 2,
          "matched": false,
          "conditions": [
            {
              "kind": "supplier-regime-not-in",
              "expected": "SimplesNacional,SimplesNacionalExcessoSublimite,Mei",
              "actual": "LucroPresumido",
              "outcome": "Passed"
            },
            {
              "kind": "item-general-regime",
              "expected": "regime geral",
              "actual": "reducao-60",
              "outcome": "Failed"
            }
          ],
          "output": "não se aplica",
          "legalSourceCodes": [
            "LC214-2025-ART346",
            "LC214-2025-ART348"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000006",
          "ruleKey": "IBS.RATE.2026-TEST-STATE",
          "ruleVersion": 1,
          "contentHash": "acfeedf5b258de43d760e27a6e9f945b0f8ff541678ced46e1309dbd23e7e76a",
          "lineIndex": 2,
          "matched": false,
          "conditions": [
            {
              "kind": "supplier-regime-not-in",
              "expected": "SimplesNacional,SimplesNacionalExcessoSublimite,Mei",
              "actual": "LucroPresumido",
              "outcome": "Passed"
            },
            {
              "kind": "item-general-regime",
              "expected": "regime geral",
              "actual": "reducao-60",
              "outcome": "Failed"
            }
          ],
          "output": "não se aplica",
          "legalSourceCodes": [
            "LC214-2025-ART343",
            "LC214-2025-ART348"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000007",
          "ruleKey": "IBSCBS.NOTE.2026-SIMPLES",
          "ruleVersion": 1,
          "contentHash": "32489294dadefad3bb8bd6f58c1ee2cd272d8c43f3d1d6d95edb36bd7c39a776",
          "lineIndex": null,
          "matched": false,
          "conditions": [
            {
              "kind": "supplier-regime-in",
              "expected": "SimplesNacional,SimplesNacionalExcessoSublimite,Mei",
              "actual": "LucroPresumido",
              "outcome": "Failed"
            }
          ],
          "output": "não se aplica",
          "legalSourceCodes": [
            "LC214-2025-ART348"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000008",
          "ruleKey": "IBSCBS.NOTE.2026-COMPENSATION",
          "ruleVersion": 1,
          "contentHash": "d6130ea8c549084ba1e5cd517915b05c603564f96a2363057634a2c01034755b",
          "lineIndex": null,
          "matched": true,
          "conditions": [
            {
              "kind": "direction-is",
              "expected": "Outbound",
              "actual": "Outbound",
              "outcome": "Passed"
            },
            {
              "kind": "supplier-regime-not-in",
              "expected": "SimplesNacional,SimplesNacionalExcessoSublimite,Mei",
              "actual": "LucroPresumido",
              "outcome": "Passed"
            }
          ],
          "output": "nota IBSCBS-2026-COMPENSATION",
          "legalSourceCodes": [
            "LC214-2025-ART348"
          ],
          "isHypothesis": false
        },
        {
          "ruleVersionId": "7a9c3e00-0000-4000-8000-000000000009",
          "ruleKey": "IBSCBS.NOTE.2026-WAIVER",
          "ruleVersion": 1,
          "contentHash": "de2ce6c335fa29976a102f8a07f35c28700195947a4ae8ad1565918f6909f294",
          "lineIndex": null,
          "matched": false,
          "conditions": [
            {
              "kind": "direction-is",
              "expected": "Outbound",
              "actual": "Outbound",
              "outcome": "Passed"
            },
            {
              "kind": "supplier-regime-not-in",
              "expected": "SimplesNacional,SimplesNacionalExcessoSublimite,Mei",
              "actual": "LucroPresumido",
              "outcome": "Passed"
            },
            {
              "kind": "profile-flag-is",
              "expected": "accessoryObligationsCompliant=True",
              "actual": "accessoryObligationsCompliant não declarado no perfil",
              "outcome": "Unknown"
            }
          ],
          "output": "não se aplica",
          "legalSourceCodes": [
            "LC214-2025-ART348"
          ],
          "isHypothesis": false
        }
      ],
      "notApplied": []
    },
    "charged": 1740,
    "taxTotal": 7.9,
    "creditEstimate": 0,
    "effectiveCost": 7.9,
    "taxDeltaVsBaseline": -0.5,
    "effectiveCostDeltaVsBaseline": -0.5,
    "noCashEffectInPeriod": true,
    "hypotheses": []
  }
];
