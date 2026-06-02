import React, { useEffect, useMemo, useState } from 'react';
import { Layout, Menu, Tabs } from 'antd';
import {
  DashboardOutlined, StockOutlined, RobotOutlined, SettingOutlined, ThunderboltOutlined,
  ApartmentOutlined, HistoryOutlined, ControlOutlined, NodeIndexOutlined, SearchOutlined,
  FundOutlined, FireOutlined, FileSearchOutlined, MessageOutlined, FileTextOutlined,
  LineChartOutlined, DeploymentUnitOutlined, ToolOutlined,
} from '@ant-design/icons';
import { useNavigate, useLocation, useOutlet } from 'react-router-dom';
import { KeepAlive, useKeepAliveRef } from 'keepalive-for-react';

const { Header, Sider, Content } = Layout;

/** 每个路由的标题与图标（菜单 + 标签页共用） */
const ROUTE_META: Record<string, { label: string; icon: React.ReactNode }> = {
  '/': { label: '总览', icon: <DashboardOutlined /> },
  '/selection': { label: '选股', icon: <FundOutlined /> },
  '/selection-history': { label: '历史选股', icon: <HistoryOutlined /> },
  '/review': { label: '每日复盘', icon: <FileSearchOutlined /> },
  '/sector': { label: '板块资金', icon: <FireOutlined /> },
  '/stock-detail': { label: '股票详情', icon: <SearchOutlined /> },
  '/auto-trading': { label: '自主交易', icon: <ThunderboltOutlined /> },
  '/positions': { label: '持仓', icon: <StockOutlined /> },
  '/strategy': { label: '策略', icon: <SettingOutlined /> },
  '/agents': { label: 'Agent', icon: <RobotOutlined /> },
  '/ai-chat': { label: 'AI 对话', icon: <MessageOutlined /> },
  '/workflow': { label: 'Workflow', icon: <NodeIndexOutlined /> },
  '/memory': { label: 'Agent记忆', icon: <HistoryOutlined /> },
  '/knowledge': { label: '知识图谱', icon: <ApartmentOutlined /> },
  '/llm': { label: 'LLM管理', icon: <RobotOutlined /> },
  '/prompts': { label: 'Prompt', icon: <FileTextOutlined /> },
  '/observability': { label: '系统观测', icon: <ControlOutlined /> },
};

/** 一级分组 → 二级路由 */
const GROUPS: { key: string; label: string; icon: React.ReactNode; children: string[] }[] = [
  { key: 'g-decision', label: '选股决策', icon: <LineChartOutlined />, children: ['/selection', '/selection-history', '/review', '/sector', '/stock-detail'] },
  { key: 'g-trade', label: '交易', icon: <ThunderboltOutlined />, children: ['/auto-trading', '/positions', '/strategy'] },
  { key: 'g-ai', label: '智能体', icon: <DeploymentUnitOutlined />, children: ['/agents', '/ai-chat', '/workflow', '/memory'] },
  { key: 'g-knowledge', label: '知识库', icon: <ApartmentOutlined />, children: ['/knowledge'] },
  { key: 'g-sys', label: '系统', icon: <ToolOutlined />, children: ['/llm', '/prompts', '/observability'] },
];

const leaf = (path: string) => ({ key: path, icon: ROUTE_META[path]?.icon, label: ROUTE_META[path]?.label ?? path });

const menuItems = [
  leaf('/'),
  ...GROUPS.map((g) => ({ key: g.key, icon: g.icon, label: g.label, children: g.children.map(leaf) })),
];

/** 路径 → 所属一级分组 key（用于自动展开） */
const groupOfPath = (path: string) => GROUPS.find((g) => g.children.includes(path))?.key;

const MainLayout: React.FC = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const outlet = useOutlet();
  const aliveRef = useKeepAliveRef();
  const path = location.pathname;

  const [openTabs, setOpenTabs] = useState<string[]>(['/']);
  const [openKeys, setOpenKeys] = useState<string[]>(() => {
    const g = groupOfPath(path);
    return g ? [g] : [];
  });

  // 路由变化：补开标签页 + 自动展开所属分组
  useEffect(() => {
    setOpenTabs((tabs) => (tabs.includes(path) ? tabs : [...tabs, path]));
    const g = groupOfPath(path);
    if (g) setOpenKeys((keys) => (keys.includes(g) ? keys : [...keys, g]));
  }, [path]);

  const tabItems = useMemo(
    () => openTabs.map((p) => ({ key: p, label: ROUTE_META[p]?.label ?? p, closable: p !== '/' })),
    [openTabs],
  );

  const removeTab = (target: string) => {
    setOpenTabs((tabs) => {
      const idx = tabs.indexOf(target);
      const next = tabs.filter((t) => t !== target);
      if (target === path) {
        const fallback = next[idx] ?? next[idx - 1] ?? '/';
        navigate(fallback);
      }
      return next;
    });
    // 释放该标签页的 keep-alive 缓存
    aliveRef.current?.destroy(target);
  };

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider theme="dark" width={200} breakpoint="lg" collapsedWidth={0}>
        <div style={{ height: 48, margin: 12, color: '#fff', fontSize: 18, fontWeight: 'bold', textAlign: 'center', lineHeight: '48px' }}>
          AI-Stock
        </div>
        <Menu
          theme="dark"
          mode="inline"
          selectedKeys={[path]}
          openKeys={openKeys}
          onOpenChange={(keys) => setOpenKeys(keys as string[])}
          items={menuItems}
          onClick={({ key }) => { if (!key.startsWith('g-')) navigate(key); }}
        />
      </Sider>
      <Layout>
        <Header style={{ background: '#fff', padding: '0 24px', fontSize: 16, fontWeight: 600 }}>
          AI 自主交易系统
        </Header>
        <Tabs
          type="editable-card"
          hideAdd
          activeKey={path}
          items={tabItems}
          onChange={(key) => navigate(key)}
          onEdit={(targetKey, action) => { if (action === 'remove') removeTab(targetKey as string); }}
          style={{ padding: '6px 12px 0', background: '#fff', borderBottom: '1px solid #f0f0f0' }}
          tabBarStyle={{ marginBottom: 0 }}
        />
        <Content style={{ margin: 16, padding: 20, background: '#fff', minHeight: 280, borderRadius: 8 }}>
          <KeepAlive activeCacheKey={path} aliveRef={aliveRef} max={20}>
            {outlet}
          </KeepAlive>
        </Content>
      </Layout>
    </Layout>
  );
};

export default MainLayout;
