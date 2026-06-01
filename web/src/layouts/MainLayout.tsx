import React from 'react';
import { Layout, Menu } from 'antd';
import {
  DashboardOutlined,
  StockOutlined,
  RobotOutlined,
  SettingOutlined,
  ThunderboltOutlined,
  ApartmentOutlined,
  HistoryOutlined,
  ControlOutlined,
  NodeIndexOutlined,
  SearchOutlined,
  FundOutlined,
  FireOutlined,
  MessageOutlined,
  FileTextOutlined,
} from '@ant-design/icons';
import { useNavigate, useLocation, Outlet } from 'react-router-dom';

const { Header, Sider, Content } = Layout;

const menuItems = [
  {
    key: '/',
    icon: <DashboardOutlined />,
    label: 'Dashboard',
  },
  {
    key: '/auto-trading',
    icon: <ThunderboltOutlined />,
    label: '自主交易',
  },
  {
    key: '/positions',
    icon: <StockOutlined />,
    label: '持仓',
  },
  {
    key: '/selection',
    icon: <FundOutlined />,
    label: '选股',
  },
  {
    key: '/sector',
    icon: <FireOutlined />,
    label: '板块资金',
  },
  {
    key: '/agents',
    icon: <RobotOutlined />,
    label: 'Agent',
  },
  {
    key: '/strategy',
    icon: <SettingOutlined />,
    label: '策略',
  },
  {
    key: '/knowledge',
    icon: <ApartmentOutlined />,
    label: '知识图谱',
  },
  {
    key: '/memory',
    icon: <HistoryOutlined />,
    label: 'Agent记忆',
  },
  {
    key: '/stock-detail',
    icon: <SearchOutlined />,
    label: '股票详情',
  },
  {
    key: '/workflow',
    icon: <NodeIndexOutlined />,
    label: 'Workflow',
  },
  {
    key: '/llm',
    icon: <RobotOutlined />,
    label: 'LLM管理',
  },
  {
    key: '/observability',
    icon: <ControlOutlined />,
    label: '系统观测',
  },
  {
    key: '/ai-chat',
    icon: <MessageOutlined />,
    label: 'AI 对话',
  },
  {
    key: '/prompts',
    icon: <FileTextOutlined />,
    label: 'Prompt',
  },
];

const MainLayout: React.FC = () => {
  const navigate = useNavigate();
  const location = useLocation();

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider theme="dark" width={200}>
        <div style={{ height: 32, margin: 16, color: 'white', fontSize: 18, fontWeight: 'bold', textAlign: 'center' }}>
          AI-Stock
        </div>
        <Menu
          theme="dark"
          mode="inline"
          selectedKeys={[location.pathname]}
          items={menuItems}
          onClick={({ key }) => navigate(key)}
        />
      </Sider>
      <Layout>
        <Header style={{ background: '#fff', padding: '0 24px', fontSize: 16 }}>
          AI自主交易系统
        </Header>
        <Content style={{ margin: 24, padding: 24, background: '#fff', minHeight: 280 }}>
          <Outlet />
        </Content>
      </Layout>
    </Layout>
  );
};

export default MainLayout;
