import { motion } from "framer-motion";
import { BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, Legend } from "recharts";

interface VolumeData {
  day: string;
  Invoices: number;
  "Purchase Orders": number;
  "Tour Sheets": number;
  Timesheets: number;
}

const colors = {
  Invoices: "hsl(192,95%,55%)",
  "Purchase Orders": "hsl(270,60%,55%)",
  "Tour Sheets": "hsl(160,70%,45%)",
  Timesheets: "hsl(38,92%,55%)",
};

function CustomTooltip({ active, payload, label }: any) {
  if (!active || !payload) return null;
  return (
    <div className="glass-panel p-3 !rounded-lg text-xs">
      <p className="font-semibold text-foreground mb-1.5">{label}</p>
      {payload.map((p: any) => (
        <div key={p.name} className="flex items-center gap-2 py-0.5">
          <span className="h-2 w-2 rounded-full" style={{ background: p.color }} />
          <span className="text-muted-foreground">{p.name}:</span>
          <span className="font-medium text-foreground">{p.value}</span>
        </div>
      ))}
    </div>
  );
}

export function VolumeChart({ data }: { data: VolumeData[] }) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: 0.6, duration: 0.5 }}
      className="glass-panel p-5"
    >
      <h2 className="text-lg font-semibold text-foreground mb-1">7-Day Volume</h2>
      <p className="text-sm text-muted-foreground mb-5">Document processing by type</p>

      <ResponsiveContainer width="100%" height={220}>
        <BarChart data={data} barCategoryGap="20%">
          <XAxis dataKey="day" tick={{ fill: "hsl(215,20%,55%)", fontSize: 12 }} axisLine={false} tickLine={false} />
          <YAxis tick={{ fill: "hsl(215,20%,55%)", fontSize: 12 }} axisLine={false} tickLine={false} width={30} />
          <Tooltip content={<CustomTooltip />} cursor={{ fill: "hsl(220,50%,12%,0.5)" }} />
          <Legend
            iconType="circle"
            iconSize={8}
            wrapperStyle={{ fontSize: 11, paddingTop: 8 }}
            formatter={(value: string) => <span style={{ color: "hsl(215,20%,65%)" }}>{value}</span>}
          />
          {Object.entries(colors).map(([key, color]) => (
            <Bar key={key} dataKey={key} fill={color} radius={[3, 3, 0, 0]} maxBarSize={24} />
          ))}
        </BarChart>
      </ResponsiveContainer>
    </motion.div>
  );
}
