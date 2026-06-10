import React, { useEffect, useMemo, useState } from 'react';
import { Layout, Menu, Tabs, Dropdown, Tag, Button, Tooltip, theme } from 'antd';
import {
  DashboardOutlined, StockOutlined, RobotOutlined, SettingOutlined, ThunderboltOutlined,
  ApartmentOutlined, HistoryOutlined, ControlOutlined, NodeIndexOutlined, SearchOutlined,
  FundOutlined, FireOutlined, FileSearchOutlined, MessageOutlined, FileTextOutlined,
  LineChartOutlined, DeploymentUnitOutlined, ToolOutlined, SlidersOutlined, ExperimentOutlined,
  PartitionOutlined, MenuFoldOutlined, MenuUnfoldOutlined, BulbOutlined, BulbFilled,
} from '@ant-design/icons';
import { useNavigate, useLocation, useOutlet } from 'react-router-dom';
import { KeepAlive, useKeepAliveRef } from 'keepalive-for-react';
import { useThemeMode } from '../contexts/ThemeContext';
import { getTradingStatus } from '../api';

const { Header, Sider, Content } = Layout;

/** 每个路由的标题与图标（菜单 + 标签页共用） */
const ROUTE_META: Record<string, { label: string; icon: React.ReactNode }> = {
  '/': { label: '总览', icon: <DashboardOutlined /> },
  '/selection': { label: '选股', icon: <FundOutlined /> },
  '/selection-history': { label: '历史选股', icon: <HistoryOutlined /> },
  '/selection-config': { label: '选股配置', icon: <SlidersOutlined /> },
  '/strategy-manager': { label: '策略管理', icon: <PartitionOutlined /> },
  '/selection-backtest': { label: '选股回测', icon: <ExperimentOutlined /> },
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
  '/worker-config': { label: '任务配置', icon: <ToolOutlined /> },
};

/** 一级分组 → 二级路由 */
const GROUPS: { key: string; label: string; icon: React.ReactNode; children: string[] }[] = [
  { key: 'g-decision', label: '选股决策', icon: <LineChartOutlined />, children: ['/selection', '/selection-history', '/selection-config', '/strategy-manager', '/selection-backtest', '/review', '/sector', '/stock-detail'] },
  { key: 'g-trade', label: '交易', icon: <ThunderboltOutlined />, children: ['/auto-trading', '/positions', '/strategy'] },
  { key: 'g-ai', label: '智能体', icon: <DeploymentUnitOutlined />, children: ['/agents', '/ai-chat', '/workflow', '/memory'] },
  { key: 'g-knowledge', label: '知识库', icon: <ApartmentOutlined />, children: ['/knowledge'] },
  { key: 'g-sys', label: '系统', icon: <ToolOutlined />, children: ['/llm', '/prompts', '/worker-config', '/observability'] },
];

const leaf = (path: string) => ({ key: path, icon: ROUTE_META[path]?.icon, label: ROUTE_META[path]?.label ?? path });

const menuItems = [
  leaf('/'),
  ...GROUPS.map((g) => ({ key: g.key, icon: g.icon, label: g.label, children: g.children.map(leaf) })),
];

/** 路径 → 所属一级分组 key（用于自动展开） */
const groupOfPath = (path: string) => GROUPS.find((g) => g.children.includes(path))?.key;

const TABS_STORAGE_KEY = 'aistock.openTabs';

/** 读取已持久化的标签页（过滤掉已失效的路由） */
const loadTabs = (): string[] => {
  try {
    const raw = JSON.parse(localStorage.getItem(TABS_STORAGE_KEY) || '[]') as string[];
    const valid = raw.filter((p) => p === '/' || p in ROUTE_META);
    return valid.includes('/') ? valid : ['/', ...valid];
  } catch {
    return ['/'];
  }
};

const MainLayout: React.FC = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const outlet = useOutlet();
  const aliveRef = useKeepAliveRef();
  const path = location.pathname;
  const { token } = theme.useToken();
  const { dark, toggle } = useThemeMode();

  const [collapsed, setCollapsed] = useState(false);
  const [openTabs, setOpenTabs] = useState<string[]>(loadTabs);
  const [openKeys, setOpenKeys] = useState<string[]>(() => {
    const g = groupOfPath(path);
    return g ? [g] : [];
  });
  const [tradeStatus, setTradeStatus] = useState<{ mode: string; halted: boolean } | null>(null);

  // 路由变化：补开标签页 + 自动展开所属分组
  useEffect(() => {
    setOpenTabs((tabs) => (tabs.includes(path) ? tabs : [...tabs, path]));
    const g = groupOfPath(path);
    if (g) setOpenKeys((keys) => (keys.includes(g) ? keys : [...keys, g]));
  }, [path]);

  // 标签页持久化
  useEffect(() => {
    localStorage.setItem(TABS_STORAGE_KEY, JSON.stringify(openTabs));
  }, [openTabs]);

  // 交易闸门状态（模式/熔断），每 30s 刷新
  useEffect(() => {
    let alive = true;
    const fetchStatus = () =>
      getTradingStatus()
        .then((r) => { if (alive) setTradeStatus(r.data); })
        .catch(() => { if (alive) setTradeStatus(null); });
    fetchStatus();
    const timer = setInterval(fetchStatus, 30000);
    return () => { alive = false; clearInterval(timer); };
  }, []);

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
    aliveRef.current?.destroy(target);
  };

  // 关闭其它标签（保留首页与目标标签）
  const closeOthers = (keep: string) => {
    setOpenTabs((tabs) => {
      const next = tabs.filter((t) => t === '/' || t === keep);
      tabs.forEach((t) => { if (!next.includes(t)) aliveRef.current?.destroy(t); });
      return next;
    });
    if (path !== '/' && path !== keep) navigate(keep);
  };

  // 关闭全部（仅保留首页）
  const closeAll = () => {
    setOpenTabs((tabs) => {
      tabs.forEach((t) => { if (t !== '/') aliveRef.current?.destroy(t); });
      return ['/'];
    });
    navigate('/');
  };

  const tabItems = useMemo(
    () =>
      openTabs.map((p) => ({
        key: p,
        closable: p !== '/',
        label: (
          <Dropdown
            trigger={['contextMenu']}
            menu={{
              items: [
                { key: 'close', label: '关闭', disabled: p === '/' },
                { key: 'others', label: '关闭其它' },
                { key: 'all', label: '关闭全部' },
              ],
              onClick: ({ key }) => {
                if (key === 'close') removeTab(p);
                else if (key === 'others') closeOthers(p);
                else if (key === 'all') closeAll();
              },
            }}
          >
            <span>{ROUTE_META[p]?.label ?? p}</span>
          </Dropdown>
        ),
      })),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [openTabs],
  );

  const modeTag = tradeStatus && (
    tradeStatus.mode === 'Live'
      ? <Tag color="red">实盘</Tag>
      : <Tag color="blue">模拟</Tag>
  );

  return (
    <Layout style={{ height: '100vh' }}>
      <Sider
        theme="dark"
        width={200}
        collapsible
        collapsed={collapsed}
        trigger={null}
        breakpoint="lg"
        onBreakpoint={(broken) => setCollapsed(broken)}
        style={{ overflow: 'auto' }}
      >
        <div style={{ height: 48, margin: 12, color: '#fff', fontSize: collapsed ? 14 : 18, fontWeight: 'bold', textAlign: 'center', lineHeight: '48px', whiteSpace: 'nowrap', overflow: 'hidden' }}>
          {collapsed ? 'AI' : 'AI-Stock'}
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
        <Header style={{ display: 'flex', alignItems: 'center', background: token.colorBgContainer, padding: '0 16px', borderBottom: `1px solid ${token.colorBorderSecondary}` }}>
          <Button
            type="text"
            icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
            onClick={() => setCollapsed((c) => !c)}
            style={{ fontSize: 16 }}
          />
          <span style={{ fontSize: 16, fontWeight: 600, marginLeft: 8 }}>AI 自主交易系统</span>
          <div style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: 8 }}>
            {modeTag}
            {tradeStatus?.halted && <Tag color="red">熔断</Tag>}
            <Tooltip title={dark ? '切换浅色' : '切换深色'}>
              <Button type="text" icon={dark ? <BulbFilled /> : <BulbOutlined />} onClick={toggle} />
            </Tooltip>
          </div>
        </Header>
        <Tabs
          type="editable-card"
          hideAdd
          activeKey={path}
          items={tabItems}
          onChange={(key) => navigate(key)}
          onEdit={(targetKey, action) => { if (action === 'remove') removeTab(targetKey as string); }}
          style={{ flex: 'none', padding: '6px 12px 0', background: token.colorBgContainer, borderBottom: `1px solid ${token.colorBorderSecondary}` }}
          tabBarStyle={{ marginBottom: 0 }}
        />
        <Content style={{ flex: 1, overflow: 'auto', margin: 16, padding: 20, background: token.colorBgContainer, borderRadius: 8 }}>
          <KeepAlive activeCacheKey={path} aliveRef={aliveRef} max={20}>
            {outlet}
          </KeepAlive>
        </Content>
      </Layout>
    </Layout>
  );
};

export default MainLayout;
