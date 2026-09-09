import { messages } from "@/lib/messages";

export default function Home() {
  const t = messages.tr;
  return (
    <main className="shell">
      <header><div className="brand"><span className="brandMark">M</span>{t.title}</div><span className="status"><i />{t.syncValue}</span></header>
      <section className="hero"><p className="eyebrow">{t.eyebrow}</p><h1>{t.heading}</h1><p className="intro">{t.intro}</p></section>
      <section className="dashboard" aria-label={t.today}>
        <article className="todayCard"><div className="sectionTitle"><h2>{t.today}</h2><span className="date">09.09</span></div><div className="doseRow"><div><small>{t.person}</small><h3>{t.medication}</h3><p>{t.schedule}</p></div><span className="due">{t.due}</span></div></article>
        <article className="metric stock"><small>{t.stock}</small><strong>{t.stockValue}</strong><span>{t.forecast}</span></article>
        <article className="metric sync"><small>{t.sync}</small><strong>{t.syncValue}</strong><span>{t.syncHint}</span></article>
      </section>
      <section className="principles"><article><span>01</span><h2>{t.audit}</h2><p>{t.auditHint}</p></article><article><span>02</span><h2>{t.safety}</h2><p>{t.safetyHint}</p></article></section>
    </main>
  );
}
