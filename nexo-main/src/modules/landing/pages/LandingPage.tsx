import { LandingNav }          from "../components/LandingNav";
import { LandingHero }         from "../components/LandingHero";
import { LandingPain }         from "../components/LandingPain";
import { LandingSolution }     from "../components/LandingSolution";
import { LandingForWhom }      from "../components/LandingForWhom";
import { LandingHowItWorks }   from "../components/LandingHowItWorks";
import { LandingDifferentials} from "../components/LandingDifferentials";
import { LandingCtaBlock }     from "../components/LandingCtaBlock";
import { LandingFaq }          from "../components/LandingFaq";
import { LandingCtaFinal }     from "../components/LandingCtaFinal";
import { LandingFooter }       from "../components/LandingFooter";

export default function LandingPage() {
  return (
    <div className="min-h-screen flex flex-col bg-orken-navy">
      <LandingNav />
      <main className="flex-1">
        <LandingHero />
        <LandingPain />
        <LandingSolution />
        <LandingForWhom />
        <LandingHowItWorks />
        <LandingDifferentials />
        <LandingCtaBlock />
        <LandingFaq />
        <LandingCtaFinal />
      </main>
      <LandingFooter />
    </div>
  );
}
