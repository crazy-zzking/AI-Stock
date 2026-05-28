import React from 'react';
import { Layout, Menu } from 'antd';
import {
  DashboardOutlined,
  StockOutlined,
  RobotOutlined,
  SettingOutlined,
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
    key: '/positions',
    icon: <StockOutlined />,
    label: '持仓',
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
