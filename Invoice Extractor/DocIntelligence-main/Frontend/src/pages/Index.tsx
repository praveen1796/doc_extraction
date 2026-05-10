import { DashboardHeader } from "@/components/dashboard/DashboardHeader";
import { AILiveStrip } from "@/components/dashboard/AILiveStrip";
import { AISummaryStrip } from "@/components/dashboard/AISummaryStrip";
import { KPICard } from "@/components/dashboard/KPICard";
import { PipelinePanel } from "@/components/dashboard/PipelinePanel";
import { ExtractionsTable } from "@/components/dashboard/ExtractionsTable";
import { InsightsPanel } from "@/components/dashboard/InsightsPanel";
import { DocTypesPanel } from "@/components/dashboard/DocTypesPanel";
import { HealthPanel } from "@/components/dashboard/HealthPanel";
import { VolumeChart } from "@/components/dashboard/VolumeChart";
import {
  mockKPIs,
  mockPipelineStages,
  mockExtractions,
  mockInsights,
  mockDocTypes,
  mockVolumeData,
  mockHealth,
} from "@/data/mockData";

const Index = () => {
  return (
    <div className="min-h-screen bg-background p-4 md:p-6 lg:p-8 max-w-[1600px] mx-auto space-y-5">
      <DashboardHeader />
      <AILiveStrip />
      <AISummaryStrip />

      {/* KPI Cards */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        {mockKPIs.map((kpi, i) => (
          <KPICard key={kpi.label} {...kpi} icon={kpi.icon as any} index={i} />
        ))}
      </div>

      {/* Pipeline */}
      <PipelinePanel stages={mockPipelineStages} />

      {/* Extractions + Insights */}
      <div className="grid grid-cols-1 xl:grid-cols-3 gap-5">
        <div className="xl:col-span-2">
          <ExtractionsTable data={mockExtractions} />
        </div>
        <InsightsPanel insights={mockInsights} />
      </div>

      {/* Bottom row: Doc Types, Health, Volume */}
      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-5">
        <DocTypesPanel data={mockDocTypes} />
        <HealthPanel data={mockHealth} />
        <VolumeChart data={mockVolumeData} />
      </div>
    </div>
  );
};

export default Index;
