import { LandingNav }        from "../components/LandingNav";
import { LandingHero }       from "../components/LandingHero";
import { LandingPain }       from "../components/LandingPain";
import { LandingCore }       from "../components/LandingSolution";
import { LandingMenu }       from "../components/LandingForWhom";
import { LandingBuild }      from "../components/LandingBuild";
import { LandingHowItWorks } from "../components/LandingHowItWorks";
import { LandingFaq }        from "../components/LandingFaq";
import { LandingCtaFinal }   from "../components/LandingCtaFinal";
import { LandingFooter }     from "../components/LandingFooter";

export default function LandingPage() {
  return (
    <div className="min-h-screen flex flex-col bg-orken-navy">
      <LandingNav />
      <main className="flex-1 pt-[60px]">
        <LandingHero />
        <LandingPain />
        <LandingCore />
        <LandingMenu />
        <LandingBuild />
        <LandingHowItWorks />
        <LandingFaq />
        <LandingCtaFinal />
      </main>
      <LandingFooter />
    </div>
  );
}
