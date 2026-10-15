import { TitlesView } from "../components/TitlesView";

/**
 * Contas a receber: o que os clientes devem, por vencimento. Receber dá baixa total ou parcial —
 * o que sobra continua em aberto —, com juros, multa e desconto lançados à parte no financeiro.
 */
export default function ContasReceberPage() {
  return <TitlesView direction="Receivable" />;
}
