--
-- PostgreSQL database dump
--

\restrict kLvFGy1iafBp6qPDsQkP7f6PsvcRk6P1tjbl03wzJYfgwqkNxmxyjCEfixf0YcE

-- Dumped from database version 16.11
-- Dumped by pg_dump version 16.11

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: nexo; Type: SCHEMA; Schema: -; Owner: nexo_user
--

CREATE SCHEMA nexo;


ALTER SCHEMA nexo OWNER TO nexo_user;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: __ef_migrations_history; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL
);


ALTER TABLE nexo.__ef_migrations_history OWNER TO nexo_user;

--
-- Name: app_settings; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.app_settings (
    id uuid NOT NULL,
    company_settings_json jsonb DEFAULT '{}'::jsonb NOT NULL,
    operation_settings_json jsonb DEFAULT '{}'::jsonb NOT NULL,
    inventory_settings_json jsonb DEFAULT '{}'::jsonb NOT NULL,
    commission_settings_json jsonb DEFAULT '{}'::jsonb NOT NULL,
    pos_settings_json jsonb DEFAULT '{}'::jsonb NOT NULL,
    system_settings_json jsonb DEFAULT '{}'::jsonb NOT NULL,
    "TenantId1" uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.app_settings OWNER TO nexo_user;

--
-- Name: audit_records; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.audit_records (
    id uuid NOT NULL,
    tenant_id uuid,
    action_type character varying(100) NOT NULL,
    severity character varying(20) NOT NULL,
    actor_id uuid,
    actor_name character varying(150),
    actor_type character varying(30) DEFAULT 'user'::character varying NOT NULL,
    entity_type character varying(80) NOT NULL,
    entity_id character varying(100) NOT NULL,
    description character varying(500) NOT NULL,
    metadata_json jsonb,
    ip_address character varying(45),
    created_at timestamp with time zone NOT NULL
);


ALTER TABLE nexo.audit_records OWNER TO nexo_user;

--
-- Name: cash_movements; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.cash_movements (
    id uuid NOT NULL,
    cash_session_id uuid NOT NULL,
    movement_type character varying(20) NOT NULL,
    amount numeric(18,2) NOT NULL,
    description character varying(500) NOT NULL,
    reference_type character varying(50),
    reference_id uuid,
    created_by_user_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.cash_movements OWNER TO nexo_user;

--
-- Name: cash_sessions; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.cash_sessions (
    id uuid NOT NULL,
    status character varying(20) NOT NULL,
    opened_by_user_id uuid NOT NULL,
    closed_by_user_id uuid,
    opening_balance numeric(18,2) NOT NULL,
    closing_balance numeric(18,2),
    opened_at timestamp with time zone NOT NULL,
    closed_at timestamp with time zone,
    notes character varying(500),
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL,
    store_id uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE nexo.cash_sessions OWNER TO nexo_user;

--
-- Name: categories; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.categories (
    id uuid NOT NULL,
    name character varying(150) NOT NULL,
    description character varying(500),
    parent_category_id uuid,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.categories OWNER TO nexo_user;

--
-- Name: customers; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.customers (
    id uuid NOT NULL,
    person_type character varying(20) NOT NULL,
    name character varying(200) NOT NULL,
    trade_name character varying(200),
    document_type character varying(10) NOT NULL,
    document_number character varying(20) NOT NULL,
    email character varying(200),
    phone character varying(30),
    whatsapp character varying(30),
    address_json jsonb,
    credit_limit numeric(18,2),
    notes character varying(1000),
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL,
    store_id uuid
);


ALTER TABLE nexo.customers OWNER TO nexo_user;

--
-- Name: financial_accounts; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.financial_accounts (
    id uuid NOT NULL,
    code character varying(20) NOT NULL,
    name character varying(150) NOT NULL,
    account_type character varying(20) NOT NULL,
    parent_account_id uuid,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.financial_accounts OWNER TO nexo_user;

--
-- Name: financial_transactions; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.financial_transactions (
    id uuid NOT NULL,
    financial_account_id uuid NOT NULL,
    transaction_type character varying(20) NOT NULL,
    amount numeric(18,2) NOT NULL,
    description character varying(500) NOT NULL,
    due_date timestamp with time zone NOT NULL,
    paid_at timestamp with time zone,
    status character varying(20) NOT NULL,
    reference_type character varying(50),
    reference_id uuid,
    created_by_user_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.financial_transactions OWNER TO nexo_user;

--
-- Name: food_service_settings; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.food_service_settings (
    id uuid NOT NULL,
    store_type character varying(20) DEFAULT 'restaurant'::character varying NOT NULL,
    couvert_enabled boolean DEFAULT false NOT NULL,
    couvert_price_per_person numeric(18,2),
    couvert_automatic boolean DEFAULT false NOT NULL,
    service_fee_enabled boolean DEFAULT false NOT NULL,
    service_fee_percent numeric(5,2),
    order_types_enabled character varying(100) DEFAULT 'DineIn,Counter,Takeaway'::character varying NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL,
    store_id uuid NOT NULL
);


ALTER TABLE nexo.food_service_settings OWNER TO nexo_user;

--
-- Name: module_definitions; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.module_definitions (
    id uuid NOT NULL,
    key character varying(50) NOT NULL,
    name character varying(100) NOT NULL,
    description text,
    version character varying(20) NOT NULL,
    is_published boolean DEFAULT false NOT NULL,
    stripe_product_id character varying(100),
    stripe_price_monthly character varying(100),
    stripe_price_quarterly character varying(100),
    stripe_price_semiannual character varying(100),
    stripe_price_annual character varying(100),
    stripe_price_lifetime character varying(100),
    price_monthly numeric(10,2),
    price_quarterly numeric(10,2),
    price_semiannual numeric(10,2),
    price_annual numeric(10,2),
    price_lifetime numeric(10,2),
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL
);


ALTER TABLE nexo.module_definitions OWNER TO nexo_user;

--
-- Name: module_subscriptions; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.module_subscriptions (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    module_key character varying(50) NOT NULL,
    stripe_subscription_id character varying(100),
    stripe_price_id character varying(100),
    plan_type character varying(20) NOT NULL,
    status character varying(20) NOT NULL,
    current_period_start timestamp with time zone NOT NULL,
    current_period_end timestamp with time zone,
    cancel_at_period_end boolean DEFAULT false NOT NULL,
    canceled_at timestamp with time zone,
    granted_by_id uuid,
    notes character varying(500),
    "TenantId1" uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL
);


ALTER TABLE nexo.module_subscriptions OWNER TO nexo_user;

--
-- Name: platform_users; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.platform_users (
    id uuid NOT NULL,
    email character varying(200) NOT NULL,
    password_hash character varying(100) NOT NULL,
    role character varying(20) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL
);


ALTER TABLE nexo.platform_users OWNER TO nexo_user;

--
-- Name: product_modifier_groups; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.product_modifier_groups (
    id uuid NOT NULL,
    product_id uuid NOT NULL,
    name character varying(100) NOT NULL,
    is_required boolean NOT NULL,
    max_selections smallint NOT NULL,
    sort_order smallint DEFAULT 0 NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL,
    min_selections smallint DEFAULT 0 NOT NULL
);


ALTER TABLE nexo.product_modifier_groups OWNER TO nexo_user;

--
-- Name: product_modifiers; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.product_modifiers (
    id uuid NOT NULL,
    group_id uuid NOT NULL,
    name character varying(100) NOT NULL,
    price_adjustment numeric(18,2) DEFAULT 0.0 NOT NULL,
    sort_order smallint DEFAULT 0 NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.product_modifiers OWNER TO nexo_user;

--
-- Name: products; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.products (
    id uuid NOT NULL,
    code character varying(50) NOT NULL,
    barcode character varying(50),
    name character varying(200) NOT NULL,
    description character varying(1000),
    category_id uuid,
    unit character varying(10) NOT NULL,
    cost_price numeric(18,4) NOT NULL,
    sale_price numeric(18,4) NOT NULL,
    track_stock boolean DEFAULT true NOT NULL,
    min_stock_quantity numeric(18,4),
    max_stock_quantity numeric(18,4),
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL,
    store_id uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE nexo.products OWNER TO nexo_user;

--
-- Name: rest_areas; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.rest_areas (
    id uuid NOT NULL,
    name character varying(150) NOT NULL,
    description character varying(500),
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL,
    store_id uuid NOT NULL
);


ALTER TABLE nexo.rest_areas OWNER TO nexo_user;

--
-- Name: rest_order_item_modifiers; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.rest_order_item_modifiers (
    id uuid NOT NULL,
    order_item_id uuid NOT NULL,
    modifier_id uuid NOT NULL,
    label_snapshot character varying(100) NOT NULL,
    price_snapshot numeric(18,2) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.rest_order_item_modifiers OWNER TO nexo_user;

--
-- Name: rest_order_items; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.rest_order_items (
    id uuid NOT NULL,
    order_id uuid NOT NULL,
    product_id uuid NOT NULL,
    quantity numeric(18,4) NOT NULL,
    unit_price numeric(18,4) NOT NULL,
    total numeric(18,2) NOT NULL,
    notes character varying(500),
    status character varying(20) NOT NULL,
    sent_to_kitchen_at timestamp with time zone,
    prepared_at timestamp with time zone,
    delivered_at timestamp with time zone,
    cancelled_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.rest_order_items OWNER TO nexo_user;

--
-- Name: rest_orders; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.rest_orders (
    id uuid NOT NULL,
    order_number integer NOT NULL,
    status character varying(20) NOT NULL,
    table_id uuid,
    waiter_id uuid NOT NULL,
    customer_id uuid,
    sale_id uuid,
    notes character varying(500),
    opened_at timestamp with time zone NOT NULL,
    closed_at timestamp with time zone,
    cancelled_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL,
    store_id uuid NOT NULL,
    couvert_amount numeric(18,2) DEFAULT 0.0 NOT NULL,
    order_type character varying(20) DEFAULT 'DineIn'::character varying NOT NULL,
    party_size integer,
    service_fee_amount numeric(18,2) DEFAULT 0.0 NOT NULL
);


ALTER TABLE nexo.rest_orders OWNER TO nexo_user;

--
-- Name: rest_recipe_cards; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.rest_recipe_cards (
    id uuid NOT NULL,
    product_id uuid NOT NULL,
    yield numeric(18,4) NOT NULL,
    yield_unit character varying(50) NOT NULL,
    notes character varying(1000),
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL,
    store_id uuid NOT NULL
);


ALTER TABLE nexo.rest_recipe_cards OWNER TO nexo_user;

--
-- Name: rest_recipe_ingredients; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.rest_recipe_ingredients (
    id uuid NOT NULL,
    recipe_card_id uuid NOT NULL,
    ingredient_product_id uuid NOT NULL,
    quantity numeric(18,4) NOT NULL,
    unit character varying(50) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.rest_recipe_ingredients OWNER TO nexo_user;

--
-- Name: rest_tables; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.rest_tables (
    id uuid NOT NULL,
    area_id uuid NOT NULL,
    number character varying(50) NOT NULL,
    capacity integer NOT NULL,
    status character varying(20) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL,
    store_id uuid NOT NULL
);


ALTER TABLE nexo.rest_tables OWNER TO nexo_user;

--
-- Name: ret_customer_price_lists; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.ret_customer_price_lists (
    id uuid NOT NULL,
    customer_id uuid NOT NULL,
    price_list_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.ret_customer_price_lists OWNER TO nexo_user;

--
-- Name: ret_price_list_items; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.ret_price_list_items (
    id uuid NOT NULL,
    price_list_id uuid NOT NULL,
    product_id uuid NOT NULL,
    price numeric(18,4) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.ret_price_list_items OWNER TO nexo_user;

--
-- Name: ret_price_lists; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.ret_price_lists (
    id uuid NOT NULL,
    name character varying(150) NOT NULL,
    description character varying(500),
    is_default boolean DEFAULT false NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.ret_price_lists OWNER TO nexo_user;

--
-- Name: ret_purchase_items; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.ret_purchase_items (
    id uuid NOT NULL,
    purchase_id uuid NOT NULL,
    product_id uuid NOT NULL,
    quantity numeric(18,4) NOT NULL,
    unit_cost numeric(18,4) NOT NULL,
    total numeric(18,2) NOT NULL,
    notes character varying(500),
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.ret_purchase_items OWNER TO nexo_user;

--
-- Name: ret_purchases; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.ret_purchases (
    id uuid NOT NULL,
    purchase_number integer NOT NULL,
    status character varying(20) NOT NULL,
    supplier_id uuid NOT NULL,
    user_id uuid,
    total_amount numeric(18,2) NOT NULL,
    notes character varying(1000),
    invoice_number character varying(100),
    received_at timestamp with time zone,
    confirmed_at timestamp with time zone,
    cancelled_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.ret_purchases OWNER TO nexo_user;

--
-- Name: sale_items; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.sale_items (
    id uuid NOT NULL,
    sale_id uuid NOT NULL,
    product_id uuid NOT NULL,
    quantity numeric(18,4) NOT NULL,
    unit_price numeric(18,4) NOT NULL,
    cost_price numeric(18,4) NOT NULL,
    discount_amount numeric(18,2) NOT NULL,
    total numeric(18,2) NOT NULL,
    notes character varying(500),
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.sale_items OWNER TO nexo_user;

--
-- Name: sale_payments; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.sale_payments (
    id uuid NOT NULL,
    sale_id uuid NOT NULL,
    method character varying(20) NOT NULL,
    type character varying(10) NOT NULL,
    amount numeric(18,2) NOT NULL,
    due_date timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.sale_payments OWNER TO nexo_user;

--
-- Name: sales; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.sales (
    id uuid NOT NULL,
    number integer NOT NULL,
    status character varying(20) NOT NULL,
    customer_id uuid,
    sold_by_user_id uuid NOT NULL,
    cash_session_id uuid,
    subtotal numeric(18,2) NOT NULL,
    discount_amount numeric(18,2) NOT NULL,
    tax_amount numeric(18,2) NOT NULL,
    total numeric(18,2) NOT NULL,
    notes character varying(500),
    confirmed_at timestamp with time zone,
    paid_at timestamp with time zone,
    cancelled_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL,
    store_id uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL,
    surcharges_amount numeric(18,2) NOT NULL
);


ALTER TABLE nexo.sales OWNER TO nexo_user;

--
-- Name: stock_items; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.stock_items (
    id uuid NOT NULL,
    product_id uuid NOT NULL,
    current_quantity numeric(18,4) NOT NULL,
    reserved_quantity numeric(18,4) NOT NULL,
    last_movement_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL,
    store_id uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE nexo.stock_items OWNER TO nexo_user;

--
-- Name: stock_movements; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.stock_movements (
    id uuid NOT NULL,
    product_id uuid NOT NULL,
    movement_type character varying(20) NOT NULL,
    quantity numeric(18,4) NOT NULL,
    quantity_before numeric(18,4) NOT NULL,
    quantity_after numeric(18,4) NOT NULL,
    reference_type character varying(50),
    reference_id uuid,
    notes character varying(500),
    created_by_user_id uuid NOT NULL,
    cost_price_snapshot numeric(18,4),
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL,
    store_id uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL
);


ALTER TABLE nexo.stock_movements OWNER TO nexo_user;

--
-- Name: stores; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.stores (
    id uuid NOT NULL,
    module_subscription_id uuid,
    name character varying(200) NOT NULL,
    slug character varying(100) NOT NULL,
    status character varying(20) NOT NULL,
    settings_json jsonb,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.stores OWNER TO nexo_user;

--
-- Name: suppliers; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.suppliers (
    id uuid NOT NULL,
    person_type character varying(20) NOT NULL,
    name character varying(200) NOT NULL,
    trade_name character varying(200),
    document_type character varying(10) NOT NULL,
    document_number character varying(20) NOT NULL,
    email character varying(200),
    phone character varying(30),
    contact_name character varying(150),
    address_json jsonb,
    payment_terms_days integer,
    bank_info_json jsonb,
    notes character varying(1000),
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.suppliers OWNER TO nexo_user;

--
-- Name: tenants; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.tenants (
    id uuid NOT NULL,
    slug character varying(120) NOT NULL,
    company_name character varying(200) NOT NULL,
    trade_name character varying(200),
    tax_id character varying(20) NOT NULL,
    email character varying(200) NOT NULL,
    phone character varying(30),
    business_type character varying(50),
    stripe_customer_id character varying(100),
    status character varying(20) NOT NULL,
    trial_ends_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL
);


ALTER TABLE nexo.tenants OWNER TO nexo_user;

--
-- Name: users; Type: TABLE; Schema: nexo; Owner: nexo_user
--

CREATE TABLE nexo.users (
    id uuid NOT NULL,
    full_name character varying(150) NOT NULL,
    email character varying(200) NOT NULL,
    login character varying(50) NOT NULL,
    password_hash character varying(100) NOT NULL,
    phone character varying(30),
    role character varying(20) NOT NULL,
    status character varying(20) NOT NULL,
    require_password_change boolean DEFAULT false NOT NULL,
    notes character varying(500),
    last_access_at timestamp with time zone,
    password_changed_at timestamp with time zone,
    "TenantId1" uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    tenant_id uuid NOT NULL
);


ALTER TABLE nexo.users OWNER TO nexo_user;

--
-- Data for Name: __ef_migrations_history; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.__ef_migrations_history ("MigrationId", "ProductVersion") FROM stdin;
20260406233622_InitialBaseline	8.0.11
20260407045829_RemoveShadowFkRestOrderItem	8.0.11
20260411032423_AddStoreIsolation	8.0.11
20260413055721_AddRestauranteStoreIsolation	8.0.11
20260413060240_AddModifierGroups	8.0.11
20260413060908_AddRestOrderItemModifiers	8.0.11
20260413061251_AddFoodServiceSettings	8.0.11
20260413061717_UpdateRestOrderSchema	8.0.11
20260413062209_AddSurchargesAmountToSales	8.0.11
20260413062348_FixSurchargesDefaultValue	8.0.11
20260413063408_AddMinSelectionsToModifierGroups	8.0.11
\.


--
-- Data for Name: app_settings; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.app_settings (id, company_settings_json, operation_settings_json, inventory_settings_json, commission_settings_json, pos_settings_json, system_settings_json, "TenantId1", created_at, updated_at, tenant_id) FROM stdin;
f44ed32a-9607-4dd1-8cbc-1c16733724c7	{"cnpj": "", "name": "Andrade Systems", "email": "", "phone": "", "tradeName": "Andrade Systems"}	{"defaultOperator": ""}	{"minStockBehavior": "alert", "noMovementAlertDays": 30, "enableLowStockAlerts": true, "enableZeroStockAlerts": true, "enableHighRotationAlerts": false}	{"policyNotes": "", "defaultCommissionRate": 3, "enableProductCommission": false}	{"allowValueDiscount": true, "maxDiscountPercent": 20, "requireManagerAuth": true, "allowPercentDiscount": true}	{"language": "pt-BR", "dateFormat": "dd/MM/yyyy", "currencySymbol": "R$"}	\N	2026-04-17 22:20:54.50425+00	2026-04-17 22:20:54.504251+00	7e15a307-fb18-4d0c-8b09-36b7b89c992b
1a82ae9a-75f7-41a7-bf92-9b0263b58b71	{"cnpj": "", "name": "Admin Orken", "email": "admin@orkentest.com", "phone": "", "tradeName": "Admin Orken"}	{"defaultOperator": ""}	{"minStockBehavior": "alert", "noMovementAlertDays": 30, "enableLowStockAlerts": true, "enableZeroStockAlerts": true, "enableHighRotationAlerts": false}	{"policyNotes": "", "defaultCommissionRate": 3, "enableProductCommission": false}	{"allowValueDiscount": true, "maxDiscountPercent": 20, "requireManagerAuth": false, "allowPercentDiscount": true}	{"language": "pt-BR", "dateFormat": "dd/MM/yyyy", "currencySymbol": "R$"}	\N	2026-04-17 22:21:24.974794+00	2026-04-17 22:21:24.974794+00	e6c48161-d35e-4885-835a-782540ad4479
\.


--
-- Data for Name: audit_records; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.audit_records (id, tenant_id, action_type, severity, actor_id, actor_name, actor_type, entity_type, entity_id, description, metadata_json, ip_address, created_at) FROM stdin;
\.


--
-- Data for Name: cash_movements; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.cash_movements (id, cash_session_id, movement_type, amount, description, reference_type, reference_id, created_by_user_id, created_at, updated_at, tenant_id) FROM stdin;
\.


--
-- Data for Name: cash_sessions; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.cash_sessions (id, status, opened_by_user_id, closed_by_user_id, opening_balance, closing_balance, opened_at, closed_at, notes, created_at, updated_at, tenant_id, store_id) FROM stdin;
\.


--
-- Data for Name: categories; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.categories (id, name, description, parent_category_id, is_active, created_at, updated_at, tenant_id) FROM stdin;
\.


--
-- Data for Name: customers; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.customers (id, person_type, name, trade_name, document_type, document_number, email, phone, whatsapp, address_json, credit_limit, notes, is_active, created_at, updated_at, tenant_id, store_id) FROM stdin;
\.


--
-- Data for Name: financial_accounts; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.financial_accounts (id, code, name, account_type, parent_account_id, is_active, created_at, updated_at, tenant_id) FROM stdin;
633e0940-1812-4598-aefd-e4a9239b1d14	2.1	Contas a Receber	Receivable	\N	t	2026-04-17 22:20:54.69267+00	2026-04-17 22:20:54.69267+00	7e15a307-fb18-4d0c-8b09-36b7b89c992b
b662b674-2f23-46a9-9a29-c7f38db3d55e	3.1	Contas a Pagar	Payable	\N	t	2026-04-17 22:20:54.692671+00	2026-04-17 22:20:54.692671+00	7e15a307-fb18-4d0c-8b09-36b7b89c992b
ba6bf6b0-3f3c-4a2d-b498-b750e5a2f8f4	1.1	Caixa	Cash	\N	t	2026-04-17 22:20:54.69247+00	2026-04-17 22:20:54.692471+00	7e15a307-fb18-4d0c-8b09-36b7b89c992b
da108f89-9ce0-4df6-9707-346ffbc25db8	1.2	Banco	Bank	\N	t	2026-04-17 22:20:54.692669+00	2026-04-17 22:20:54.69267+00	7e15a307-fb18-4d0c-8b09-36b7b89c992b
\.


--
-- Data for Name: financial_transactions; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.financial_transactions (id, financial_account_id, transaction_type, amount, description, due_date, paid_at, status, reference_type, reference_id, created_by_user_id, created_at, updated_at, tenant_id) FROM stdin;
\.


--
-- Data for Name: food_service_settings; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.food_service_settings (id, store_type, couvert_enabled, couvert_price_per_person, couvert_automatic, service_fee_enabled, service_fee_percent, order_types_enabled, created_at, updated_at, tenant_id, store_id) FROM stdin;
\.


--
-- Data for Name: module_definitions; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.module_definitions (id, key, name, description, version, is_published, stripe_product_id, stripe_price_monthly, stripe_price_quarterly, stripe_price_semiannual, stripe_price_annual, stripe_price_lifetime, price_monthly, price_quarterly, price_semiannual, price_annual, price_lifetime, created_at, updated_at) FROM stdin;
0146c5ea-6009-4850-b991-1c1140f82393	imobiliaria	Imobiliárias	\N	1.0.0	f	\N	\N	\N	\N	\N	\N	97.00	\N	\N	870.00	1490.00	2026-04-17 22:20:54.570422+00	2026-04-17 22:20:54.570422+00
140d4884-c484-4741-acc6-da63049103a1	restaurante	Restaurantes e Bares	\N	1.0.0	f	\N	\N	\N	\N	\N	\N	97.00	\N	\N	870.00	1490.00	2026-04-17 22:20:54.570409+00	2026-04-17 22:20:54.570409+00
2da3e539-f70a-4769-b465-73d7014108af	oficina-mecanica	Oficinas Mecânicas	\N	1.0.0	f	\N	\N	\N	\N	\N	\N	79.00	\N	\N	710.00	1290.00	2026-04-17 22:20:54.570412+00	2026-04-17 22:20:54.570412+00
46c92fd3-7b5c-41b1-a10b-9a448f59fb01	academia-artes-marciais	Academias de Artes Marciais	\N	1.0.0	f	\N	\N	\N	\N	\N	\N	79.00	\N	\N	710.00	1290.00	2026-04-17 22:20:54.57041+00	2026-04-17 22:20:54.57041+00
6690c0ca-1228-462d-bb25-eb82876632fb	salao-beleza	Salões de Beleza	\N	1.0.0	f	\N	\N	\N	\N	\N	\N	69.00	\N	\N	620.00	1090.00	2026-04-17 22:20:54.570411+00	2026-04-17 22:20:54.570411+00
69eb1924-debb-4c26-b1d3-818f47bd3185	clinica-medica	Clínicas Médicas e Odontológicas	\N	1.0.0	f	\N	\N	\N	\N	\N	\N	97.00	\N	\N	870.00	1490.00	2026-04-17 22:20:54.570411+00	2026-04-17 22:20:54.570411+00
72462668-89a6-4962-bc6a-de5baf00f62b	varejo	Comércio em Geral (Varejo)	\N	1.0.0	f	\N	\N	\N	\N	\N	\N	79.00	\N	\N	710.00	1290.00	2026-04-17 22:20:54.570269+00	2026-04-17 22:20:54.57027+00
8d704bff-1e98-4ea2-8311-7fae659cccd2	academia-musculacao	Academias de Musculação	\N	1.0.0	f	\N	\N	\N	\N	\N	\N	79.00	\N	\N	710.00	1290.00	2026-04-17 22:20:54.57041+00	2026-04-17 22:20:54.57041+00
b37f10b3-282b-4688-9d89-fbaf6ada45c6	pet-shop	Pet Shops + Clínicas Veterinárias	\N	1.0.0	f	\N	\N	\N	\N	\N	\N	79.00	\N	\N	710.00	1290.00	2026-04-17 22:20:54.570411+00	2026-04-17 22:20:54.570411+00
cdcf831d-b90a-41b5-962c-89e2ebbda7db	pousada-hotel	Pousadas e Hotéis	\N	1.0.0	f	\N	\N	\N	\N	\N	\N	97.00	\N	\N	870.00	1490.00	2026-04-17 22:20:54.570412+00	2026-04-17 22:20:54.570413+00
\.


--
-- Data for Name: module_subscriptions; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.module_subscriptions (id, tenant_id, module_key, stripe_subscription_id, stripe_price_id, plan_type, status, current_period_start, current_period_end, cancel_at_period_end, canceled_at, granted_by_id, notes, "TenantId1", created_at, updated_at) FROM stdin;
0f8e7596-5893-4cf2-9385-7cc1fcba5d00	7e15a307-fb18-4d0c-8b09-36b7b89c992b	varejo	\N	\N	AdminGrant	Active	2026-04-17 22:20:54.769667+00	\N	f	\N	\N	\N	\N	2026-04-17 22:20:54.769544+00	2026-04-17 22:20:54.769544+00
6457c682-fe3e-40c0-828f-4b86c68d424e	14b78172-8c81-49ef-a72c-97696b749c58	varejo	\N	\N	AdminGrant	Active	2026-04-17 22:20:55.063672+00	\N	f	\N	\N	\N	\N	2026-04-17 22:20:55.063669+00	2026-04-17 22:20:55.06367+00
68188847-7f1a-44ac-936d-fe445ec974de	3fe9b743-c636-41dc-aea4-2d52a4045b50	restaurante	\N	\N	AdminGrant	Active	2026-04-17 22:20:55.455966+00	\N	f	\N	\N	\N	\N	2026-04-17 22:20:55.455966+00	2026-04-17 22:20:55.455966+00
db4eefb2-ca13-47ee-b4ac-1976727e4aca	3fe9b743-c636-41dc-aea4-2d52a4045b50	varejo	\N	\N	AdminGrant	Active	2026-04-17 22:20:55.455965+00	\N	f	\N	\N	\N	\N	2026-04-17 22:20:55.455964+00	2026-04-17 22:20:55.455964+00
345e29fb-b4bb-45ea-9bb5-fa2ece422236	778fd6f0-c2d8-4080-b04e-0fc32d2d1c6b	varejo	\N	\N	AdminGrant	Active	2026-04-17 22:20:55.832924+00	\N	f	\N	\N	\N	\N	2026-04-17 22:20:55.832923+00	2026-04-17 22:20:55.832923+00
9affc555-79da-4120-8c25-84ff05025da9	e6c48161-d35e-4885-835a-782540ad4479	varejo	\N	\N	AdminGrant	Active	2026-04-17 22:21:24.409798+00	\N	f	\N	\N	\N	\N	2026-04-17 22:21:24.409795+00	2026-04-17 22:21:24.409796+00
f25cff58-fa76-4f81-8dd8-352c8108714a	e6c48161-d35e-4885-835a-782540ad4479	restaurante	\N	\N	trial	Active	2026-04-17 22:22:31.05751+00	\N	f	\N	\N	\N	\N	2026-04-17 22:22:31.05751+00	2026-04-17 22:22:31.05751+00
\.


--
-- Data for Name: platform_users; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.platform_users (id, email, password_hash, role, created_at, updated_at) FROM stdin;
b85c7bd0-2134-43f4-b599-092fa3ace1dd	elias@nexo.com	$2a$12$eIdsjbEs70c7oWx4UEunL.iGSONdhvCVDBNnysGeQYcZNc7df4NHm	super_admin	2026-04-17 22:20:56.568206+00	2026-04-17 22:20:56.568206+00
\.


--
-- Data for Name: product_modifier_groups; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.product_modifier_groups (id, product_id, name, is_required, max_selections, sort_order, is_active, created_at, updated_at, tenant_id, min_selections) FROM stdin;
c0948101-e39a-41ac-8357-5f7ced509ee1	3ca7d3b0-033a-4dba-b35c-4c36bb9e0edb	Ponto da carne	t	1	1	t	2026-04-17 22:24:03.608767+00	2026-04-17 22:24:04.331401+00	e6c48161-d35e-4885-835a-782540ad4479	1
\.


--
-- Data for Name: product_modifiers; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.product_modifiers (id, group_id, name, price_adjustment, sort_order, is_active, created_at, updated_at, tenant_id) FROM stdin;
fb62c166-3e31-4baf-ba6a-6e6f75940617	c0948101-e39a-41ac-8357-5f7ced509ee1	Bem passado	0.00	1	t	2026-04-17 22:24:04.33122+00	2026-04-17 22:24:04.33122+00	e6c48161-d35e-4885-835a-782540ad4479
\.


--
-- Data for Name: products; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.products (id, code, barcode, name, description, category_id, unit, cost_price, sale_price, track_stock, min_stock_quantity, max_stock_quantity, is_active, created_at, updated_at, tenant_id, store_id) FROM stdin;
3ca7d3b0-033a-4dba-b35c-4c36bb9e0edb	PIC001	\N	Picanha	\N	\N	Un	0.0000	89.9000	t	\N	\N	t	2026-04-17 22:23:06.948239+00	2026-04-17 22:23:06.948239+00	e6c48161-d35e-4885-835a-782540ad4479	b105375b-aca0-4bec-ba6b-c0531127d3e8
\.


--
-- Data for Name: rest_areas; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.rest_areas (id, name, description, is_active, created_at, updated_at, tenant_id, store_id) FROM stdin;
b1a9c100-0c48-4716-8686-ce1d001192a0	Salao	\N	t	2026-04-17 22:23:45.545473+00	2026-04-17 22:23:45.545474+00	e6c48161-d35e-4885-835a-782540ad4479	b105375b-aca0-4bec-ba6b-c0531127d3e8
\.


--
-- Data for Name: rest_order_item_modifiers; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.rest_order_item_modifiers (id, order_item_id, modifier_id, label_snapshot, price_snapshot, created_at, updated_at, tenant_id) FROM stdin;
9f042c84-e8bd-40d6-8ffa-49fb18651ad9	1f87ef8a-e76d-4436-a711-1d335865d17d	fb62c166-3e31-4baf-ba6a-6e6f75940617	Bem passado	0.00	2026-04-17 22:30:32.320567+00	2026-04-17 22:30:32.320568+00	e6c48161-d35e-4885-835a-782540ad4479
\.


--
-- Data for Name: rest_order_items; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.rest_order_items (id, order_id, product_id, quantity, unit_price, total, notes, status, sent_to_kitchen_at, prepared_at, delivered_at, cancelled_at, created_at, updated_at, tenant_id) FROM stdin;
1f87ef8a-e76d-4436-a711-1d335865d17d	08ed9f3c-66f6-45be-bc5a-c0ca79d452ad	3ca7d3b0-033a-4dba-b35c-4c36bb9e0edb	1.0000	89.9000	89.90	Pouco sal	Ready	2026-04-17 22:31:13.588043+00	2026-04-17 22:31:14.125654+00	\N	\N	2026-04-17 22:30:32.264955+00	2026-04-17 22:31:14.125726+00	e6c48161-d35e-4885-835a-782540ad4479
\.


--
-- Data for Name: rest_orders; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.rest_orders (id, order_number, status, table_id, waiter_id, customer_id, sale_id, notes, opened_at, closed_at, cancelled_at, created_at, updated_at, tenant_id, store_id, couvert_amount, order_type, party_size, service_fee_amount) FROM stdin;
08ed9f3c-66f6-45be-bc5a-c0ca79d452ad	1	Paid	c091c340-f7ec-42f7-bf09-a4c70b1f351d	fd10e7b2-96ea-4d78-9550-a061e01beb23	\N	83404875-f703-4827-a208-cd85a055eae5	\N	2026-04-17 22:30:20.061848+00	2026-04-17 22:31:22.643482+00	\N	2026-04-17 22:30:20.061315+00	2026-04-17 22:32:09.052182+00	e6c48161-d35e-4885-835a-782540ad4479	b105375b-aca0-4bec-ba6b-c0531127d3e8	0.00	DineIn	2	0.00
\.


--
-- Data for Name: rest_recipe_cards; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.rest_recipe_cards (id, product_id, yield, yield_unit, notes, is_active, created_at, updated_at, tenant_id, store_id) FROM stdin;
\.


--
-- Data for Name: rest_recipe_ingredients; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.rest_recipe_ingredients (id, recipe_card_id, ingredient_product_id, quantity, unit, created_at, updated_at, tenant_id) FROM stdin;
\.


--
-- Data for Name: rest_tables; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.rest_tables (id, area_id, number, capacity, status, is_active, created_at, updated_at, tenant_id, store_id) FROM stdin;
c091c340-f7ec-42f7-bf09-a4c70b1f351d	b1a9c100-0c48-4716-8686-ce1d001192a0	01	4	Available	t	2026-04-17 22:23:54.804974+00	2026-04-17 22:32:09.051985+00	e6c48161-d35e-4885-835a-782540ad4479	b105375b-aca0-4bec-ba6b-c0531127d3e8
\.


--
-- Data for Name: ret_customer_price_lists; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.ret_customer_price_lists (id, customer_id, price_list_id, created_at, updated_at, tenant_id) FROM stdin;
\.


--
-- Data for Name: ret_price_list_items; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.ret_price_list_items (id, price_list_id, product_id, price, created_at, updated_at, tenant_id) FROM stdin;
\.


--
-- Data for Name: ret_price_lists; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.ret_price_lists (id, name, description, is_default, is_active, created_at, updated_at, tenant_id) FROM stdin;
\.


--
-- Data for Name: ret_purchase_items; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.ret_purchase_items (id, purchase_id, product_id, quantity, unit_cost, total, notes, created_at, updated_at, tenant_id) FROM stdin;
\.


--
-- Data for Name: ret_purchases; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.ret_purchases (id, purchase_number, status, supplier_id, user_id, total_amount, notes, invoice_number, received_at, confirmed_at, cancelled_at, created_at, updated_at, tenant_id) FROM stdin;
\.


--
-- Data for Name: sale_items; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.sale_items (id, sale_id, product_id, quantity, unit_price, cost_price, discount_amount, total, notes, created_at, updated_at, tenant_id) FROM stdin;
4a075887-282e-459d-b7c3-8a5d0aa69f5f	83404875-f703-4827-a208-cd85a055eae5	3ca7d3b0-033a-4dba-b35c-4c36bb9e0edb	1.0000	89.9000	0.0000	0.00	89.90	Pouco sal	2026-04-17 22:31:22.600976+00	2026-04-17 22:31:22.600976+00	e6c48161-d35e-4885-835a-782540ad4479
\.


--
-- Data for Name: sale_payments; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.sale_payments (id, sale_id, method, type, amount, due_date, created_at, updated_at, tenant_id) FROM stdin;
bb5ae136-f565-4efb-bc00-e6e15e06b4ab	83404875-f703-4827-a208-cd85a055eae5	Cash	Cash	89.90	\N	2026-04-17 22:32:08.953508+00	2026-04-17 22:32:08.953509+00	e6c48161-d35e-4885-835a-782540ad4479
\.


--
-- Data for Name: sales; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.sales (id, number, status, customer_id, sold_by_user_id, cash_session_id, subtotal, discount_amount, tax_amount, total, notes, confirmed_at, paid_at, cancelled_at, created_at, updated_at, tenant_id, store_id, surcharges_amount) FROM stdin;
83404875-f703-4827-a208-cd85a055eae5	1	Paid	\N	fd10e7b2-96ea-4d78-9550-a061e01beb23	\N	89.90	0.00	0.00	89.90	Comanda #1	2026-04-17 22:32:08.97739+00	2026-04-17 22:32:08.977648+00	\N	2026-04-17 22:31:22.467228+00	2026-04-17 22:32:08.977755+00	e6c48161-d35e-4885-835a-782540ad4479	b105375b-aca0-4bec-ba6b-c0531127d3e8	0.00
\.


--
-- Data for Name: stock_items; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.stock_items (id, product_id, current_quantity, reserved_quantity, last_movement_at, created_at, updated_at, tenant_id, store_id) FROM stdin;
94cfefff-7373-4acf-b92d-14d193cae75a	3ca7d3b0-033a-4dba-b35c-4c36bb9e0edb	49.0000	0.0000	2026-04-17 22:32:08.93147+00	2026-04-17 22:23:07.137548+00	2026-04-17 22:32:08.931471+00	e6c48161-d35e-4885-835a-782540ad4479	b105375b-aca0-4bec-ba6b-c0531127d3e8
\.


--
-- Data for Name: stock_movements; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.stock_movements (id, product_id, movement_type, quantity, quantity_before, quantity_after, reference_type, reference_id, notes, created_by_user_id, cost_price_snapshot, created_at, updated_at, tenant_id, store_id) FROM stdin;
270b6f8b-ab0b-42e3-80b5-179967f00834	3ca7d3b0-033a-4dba-b35c-4c36bb9e0edb	Adjustment	50.0000	0.0000	50.0000	\N	\N	Estoque inicial	fd10e7b2-96ea-4d78-9550-a061e01beb23	\N	2026-04-17 22:32:08.616213+00	2026-04-17 22:32:08.616213+00	e6c48161-d35e-4885-835a-782540ad4479	b105375b-aca0-4bec-ba6b-c0531127d3e8
e1a5e568-2692-44aa-8279-4b17e2f64846	3ca7d3b0-033a-4dba-b35c-4c36bb9e0edb	SaleOutput	1.0000	50.0000	49.0000	Sale	83404875-f703-4827-a208-cd85a055eae5	\N	fd10e7b2-96ea-4d78-9550-a061e01beb23	\N	2026-04-17 22:32:08.931485+00	2026-04-17 22:32:08.931486+00	e6c48161-d35e-4885-835a-782540ad4479	b105375b-aca0-4bec-ba6b-c0531127d3e8
\.


--
-- Data for Name: stores; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.stores (id, module_subscription_id, name, slug, status, settings_json, created_at, updated_at, tenant_id) FROM stdin;
f0aab485-11b0-4430-b3ac-46766b173a01	0f8e7596-5893-4cf2-9385-7cc1fcba5d00	Loja Principal	loja-principal	Active	\N	2026-04-17 22:20:54.879481+00	2026-04-17 22:20:54.879482+00	7e15a307-fb18-4d0c-8b09-36b7b89c992b
56db06e0-dcfa-4505-abe4-2fa477795eb8	6457c682-fe3e-40c0-828f-4b86c68d424e	Boutique Clara	boutique-clara	Active	\N	2026-04-17 22:20:55.069413+00	2026-04-17 22:20:55.069413+00	14b78172-8c81-49ef-a72c-97696b749c58
1f71997b-bfe5-4ec6-ab85-4ccb42631f5c	68188847-7f1a-44ac-936d-fe445ec974de	Mix Restaurante	mix-restaurante	Active	\N	2026-04-17 22:20:55.463933+00	2026-04-17 22:20:55.463933+00	3fe9b743-c636-41dc-aea4-2d52a4045b50
5b70efa3-486e-431e-b9b6-bf1f4a2e99f5	db4eefb2-ca13-47ee-b4ac-1976727e4aca	Mix Loja	mix-loja	Active	\N	2026-04-17 22:20:55.463722+00	2026-04-17 22:20:55.463722+00	3fe9b743-c636-41dc-aea4-2d52a4045b50
27da6560-5add-4ba8-92ac-0100fc4a1be3	345e29fb-b4bb-45ea-9bb5-fa2ece422236	Filial Centro	filial-centro	Active	\N	2026-04-17 22:20:55.838182+00	2026-04-17 22:20:55.838182+00	778fd6f0-c2d8-4080-b04e-0fc32d2d1c6b
38162750-29f5-4682-a930-7ed67d20c090	345e29fb-b4bb-45ea-9bb5-fa2ece422236	Filial Sul	filial-sul	Active	\N	2026-04-17 22:20:55.838383+00	2026-04-17 22:20:55.838383+00	778fd6f0-c2d8-4080-b04e-0fc32d2d1c6b
b105375b-aca0-4bec-ba6b-c0531127d3e8	9affc555-79da-4120-8c25-84ff05025da9	Loja Principal	loja-principal	Active	\N	2026-04-17 22:21:24.417163+00	2026-04-17 22:21:24.417163+00	e6c48161-d35e-4885-835a-782540ad4479
\.


--
-- Data for Name: suppliers; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.suppliers (id, person_type, name, trade_name, document_type, document_number, email, phone, contact_name, address_json, payment_terms_days, bank_info_json, notes, is_active, created_at, updated_at, tenant_id) FROM stdin;
\.


--
-- Data for Name: tenants; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.tenants (id, slug, company_name, trade_name, tax_id, email, phone, business_type, stripe_customer_id, status, trial_ends_at, created_at, updated_at) FROM stdin;
7e15a307-fb18-4d0c-8b09-36b7b89c992b	andrade-systems-4c8800	Andrade Systems	Andrade Systems	00.000.000/0001-00	contato@andradesystems.com.br	(00) 0000-0000	varejo	\N	Active	\N	2026-04-17 22:20:53.235559+00	2026-04-17 22:20:53.23556+00
14b78172-8c81-49ef-a72c-97696b749c58	boutique-clara-4c9d6e	Boutique Clara	Boutique Clara	11.111.111/0001-11	contato@boutiqueclara.com.br	(11) 1111-1111	varejo	\N	Active	\N	2026-04-17 22:20:55.057086+00	2026-04-17 22:20:55.057086+00
3fe9b743-c636-41dc-aea4-2d52a4045b50	grupo-mix-733b2e	Grupo Mix	Grupo Mix	22.222.222/0001-22	contato@grupomix.com.br	(22) 2222-2222	varejo	\N	Active	\N	2026-04-17 22:20:55.45121+00	2026-04-17 22:20:55.451211+00
778fd6f0-c2d8-4080-b04e-0fc32d2d1c6b	rede-norte-9a21d9	Rede Norte	Rede Norte	33.333.333/0001-33	contato@redenorte.com.br	(33) 3333-3333	varejo	\N	Active	\N	2026-04-17 22:20:55.827834+00	2026-04-17 22:20:55.827834+00
e6c48161-d35e-4885-835a-782540ad4479	admin-orken-98438b	Admin Orken	\N	1c1778377cc54c7f	admin@orkentest.com	\N	\N	\N	Active	\N	2026-04-17 22:21:24.399387+00	2026-04-17 22:21:24.399387+00
\.


--
-- Data for Name: users; Type: TABLE DATA; Schema: nexo; Owner: nexo_user
--

COPY nexo.users (id, full_name, email, login, password_hash, phone, role, status, require_password_change, notes, last_access_at, password_changed_at, "TenantId1", created_at, updated_at, tenant_id) FROM stdin;
d9d8469c-f44c-4aaa-a990-79da315ab5a6	Administrador do Sistema	admin@nexo.local	admin	$2a$12$TWIkBsY2PVl4LKxa1tCom.6uAwGcPTDAZr/6FytFXIXmRfBtelgbW	\N	Diretoria	Active	f	Usuário administrador criado automaticamente pelo sistema.	\N	\N	\N	2026-04-17 22:20:54.378394+00	2026-04-17 22:20:54.378394+00	7e15a307-fb18-4d0c-8b09-36b7b89c992b
197db25b-a9b8-4591-b988-314e00795dfc	Clara Mendes	clara@boutiqueclara.com.br	clara.boutique	$2a$12$HpzZ60ExL/.xXiWUM7mgcue81dTIBvCysxhqJYWavE.m/U21tDalq	\N	Gerente	Active	f	\N	\N	\N	\N	2026-04-17 22:20:55.441804+00	2026-04-17 22:20:55.441805+00	14b78172-8c81-49ef-a72c-97696b749c58
e71e4233-94f3-4d39-8494-17f99f2431b3	Lucas Ferreira	lucas@grupomix.com.br	lucas.mix	$2a$12$cWnKO5zDePluLhBJhaApWe0K/W/4P2nORdQZ5KAzJ1i3N1WTF6TMy	\N	Diretoria	Active	f	\N	\N	\N	\N	2026-04-17 22:20:55.818194+00	2026-04-17 22:20:55.818195+00	3fe9b743-c636-41dc-aea4-2d52a4045b50
f5871458-a1df-445e-af6a-4b2f542852f4	Ana Souza	ana@redenorte.com.br	ana.norte	$2a$12$555Pw1M6eDXxNYXAF9wvHurwXDwoLSa5hE3r0T28D9mR1/2vJ.xlq	\N	Gerente	Active	f	\N	\N	\N	\N	2026-04-17 22:20:56.189078+00	2026-04-17 22:20:56.189079+00	778fd6f0-c2d8-4080-b04e-0fc32d2d1c6b
fd10e7b2-96ea-4d78-9550-a061e01beb23	Admin Orken	admin@orkentest.com	admin@orkentest.com	$2a$12$XsBh9bdDMI/zmh.gcC6rJ.MKJVzMu9KdmUCrP6zz9uCxEWuNMZhfW	\N	Diretoria	Active	f	\N	2026-04-17 22:32:52.729128+00	\N	\N	2026-04-17 22:21:24.96857+00	2026-04-17 22:32:52.729129+00	e6c48161-d35e-4885-835a-782540ad4479
\.


--
-- Name: __ef_migrations_history PK___ef_migrations_history; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.__ef_migrations_history
    ADD CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId");


--
-- Name: app_settings PK_app_settings; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.app_settings
    ADD CONSTRAINT "PK_app_settings" PRIMARY KEY (id);


--
-- Name: audit_records PK_audit_records; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.audit_records
    ADD CONSTRAINT "PK_audit_records" PRIMARY KEY (id);


--
-- Name: cash_movements PK_cash_movements; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.cash_movements
    ADD CONSTRAINT "PK_cash_movements" PRIMARY KEY (id);


--
-- Name: cash_sessions PK_cash_sessions; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.cash_sessions
    ADD CONSTRAINT "PK_cash_sessions" PRIMARY KEY (id);


--
-- Name: categories PK_categories; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.categories
    ADD CONSTRAINT "PK_categories" PRIMARY KEY (id);


--
-- Name: customers PK_customers; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.customers
    ADD CONSTRAINT "PK_customers" PRIMARY KEY (id);


--
-- Name: financial_accounts PK_financial_accounts; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.financial_accounts
    ADD CONSTRAINT "PK_financial_accounts" PRIMARY KEY (id);


--
-- Name: financial_transactions PK_financial_transactions; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.financial_transactions
    ADD CONSTRAINT "PK_financial_transactions" PRIMARY KEY (id);


--
-- Name: food_service_settings PK_food_service_settings; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.food_service_settings
    ADD CONSTRAINT "PK_food_service_settings" PRIMARY KEY (id);


--
-- Name: module_definitions PK_module_definitions; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.module_definitions
    ADD CONSTRAINT "PK_module_definitions" PRIMARY KEY (id);


--
-- Name: module_subscriptions PK_module_subscriptions; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.module_subscriptions
    ADD CONSTRAINT "PK_module_subscriptions" PRIMARY KEY (id);


--
-- Name: platform_users PK_platform_users; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.platform_users
    ADD CONSTRAINT "PK_platform_users" PRIMARY KEY (id);


--
-- Name: product_modifier_groups PK_product_modifier_groups; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.product_modifier_groups
    ADD CONSTRAINT "PK_product_modifier_groups" PRIMARY KEY (id);


--
-- Name: product_modifiers PK_product_modifiers; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.product_modifiers
    ADD CONSTRAINT "PK_product_modifiers" PRIMARY KEY (id);


--
-- Name: products PK_products; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.products
    ADD CONSTRAINT "PK_products" PRIMARY KEY (id);


--
-- Name: rest_areas PK_rest_areas; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_areas
    ADD CONSTRAINT "PK_rest_areas" PRIMARY KEY (id);


--
-- Name: rest_order_item_modifiers PK_rest_order_item_modifiers; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_order_item_modifiers
    ADD CONSTRAINT "PK_rest_order_item_modifiers" PRIMARY KEY (id);


--
-- Name: rest_order_items PK_rest_order_items; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_order_items
    ADD CONSTRAINT "PK_rest_order_items" PRIMARY KEY (id);


--
-- Name: rest_orders PK_rest_orders; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_orders
    ADD CONSTRAINT "PK_rest_orders" PRIMARY KEY (id);


--
-- Name: rest_recipe_cards PK_rest_recipe_cards; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_recipe_cards
    ADD CONSTRAINT "PK_rest_recipe_cards" PRIMARY KEY (id);


--
-- Name: rest_recipe_ingredients PK_rest_recipe_ingredients; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_recipe_ingredients
    ADD CONSTRAINT "PK_rest_recipe_ingredients" PRIMARY KEY (id);


--
-- Name: rest_tables PK_rest_tables; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_tables
    ADD CONSTRAINT "PK_rest_tables" PRIMARY KEY (id);


--
-- Name: ret_customer_price_lists PK_ret_customer_price_lists; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.ret_customer_price_lists
    ADD CONSTRAINT "PK_ret_customer_price_lists" PRIMARY KEY (id);


--
-- Name: ret_price_list_items PK_ret_price_list_items; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.ret_price_list_items
    ADD CONSTRAINT "PK_ret_price_list_items" PRIMARY KEY (id);


--
-- Name: ret_price_lists PK_ret_price_lists; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.ret_price_lists
    ADD CONSTRAINT "PK_ret_price_lists" PRIMARY KEY (id);


--
-- Name: ret_purchase_items PK_ret_purchase_items; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.ret_purchase_items
    ADD CONSTRAINT "PK_ret_purchase_items" PRIMARY KEY (id);


--
-- Name: ret_purchases PK_ret_purchases; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.ret_purchases
    ADD CONSTRAINT "PK_ret_purchases" PRIMARY KEY (id);


--
-- Name: sale_items PK_sale_items; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.sale_items
    ADD CONSTRAINT "PK_sale_items" PRIMARY KEY (id);


--
-- Name: sale_payments PK_sale_payments; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.sale_payments
    ADD CONSTRAINT "PK_sale_payments" PRIMARY KEY (id);


--
-- Name: sales PK_sales; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.sales
    ADD CONSTRAINT "PK_sales" PRIMARY KEY (id);


--
-- Name: stock_items PK_stock_items; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.stock_items
    ADD CONSTRAINT "PK_stock_items" PRIMARY KEY (id);


--
-- Name: stock_movements PK_stock_movements; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.stock_movements
    ADD CONSTRAINT "PK_stock_movements" PRIMARY KEY (id);


--
-- Name: stores PK_stores; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.stores
    ADD CONSTRAINT "PK_stores" PRIMARY KEY (id);


--
-- Name: suppliers PK_suppliers; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.suppliers
    ADD CONSTRAINT "PK_suppliers" PRIMARY KEY (id);


--
-- Name: tenants PK_tenants; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.tenants
    ADD CONSTRAINT "PK_tenants" PRIMARY KEY (id);


--
-- Name: users PK_users; Type: CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.users
    ADD CONSTRAINT "PK_users" PRIMARY KEY (id);


--
-- Name: IX_app_settings_TenantId1; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_app_settings_TenantId1" ON nexo.app_settings USING btree ("TenantId1");


--
-- Name: IX_app_settings_tenant_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX "IX_app_settings_tenant_id" ON nexo.app_settings USING btree (tenant_id);


--
-- Name: IX_audit_records_action_type; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_audit_records_action_type" ON nexo.audit_records USING btree (action_type);


--
-- Name: IX_audit_records_actor_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_audit_records_actor_id" ON nexo.audit_records USING btree (actor_id);


--
-- Name: IX_audit_records_entity_type_entity_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_audit_records_entity_type_entity_id" ON nexo.audit_records USING btree (entity_type, entity_id);


--
-- Name: IX_audit_records_tenant_id_created_at; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_audit_records_tenant_id_created_at" ON nexo.audit_records USING btree (tenant_id, created_at);


--
-- Name: IX_cash_movements_cash_session_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_cash_movements_cash_session_id" ON nexo.cash_movements USING btree (cash_session_id);


--
-- Name: IX_cash_movements_tenant_id_cash_session_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_cash_movements_tenant_id_cash_session_id" ON nexo.cash_movements USING btree (tenant_id, cash_session_id);


--
-- Name: IX_cash_sessions_closed_by_user_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_cash_sessions_closed_by_user_id" ON nexo.cash_sessions USING btree (closed_by_user_id);


--
-- Name: IX_cash_sessions_opened_by_user_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_cash_sessions_opened_by_user_id" ON nexo.cash_sessions USING btree (opened_by_user_id);


--
-- Name: IX_cash_sessions_store_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_cash_sessions_store_id" ON nexo.cash_sessions USING btree (store_id);


--
-- Name: IX_cash_sessions_tenant_id_store_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_cash_sessions_tenant_id_store_id" ON nexo.cash_sessions USING btree (tenant_id, store_id);


--
-- Name: IX_categories_parent_category_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_categories_parent_category_id" ON nexo.categories USING btree (parent_category_id);


--
-- Name: IX_categories_tenant_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_categories_tenant_id" ON nexo.categories USING btree (tenant_id);


--
-- Name: IX_categories_tenant_id_name; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_categories_tenant_id_name" ON nexo.categories USING btree (tenant_id, name);


--
-- Name: IX_customers_store_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_customers_store_id" ON nexo.customers USING btree (store_id);


--
-- Name: IX_customers_tenant_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_customers_tenant_id" ON nexo.customers USING btree (tenant_id);


--
-- Name: IX_customers_tenant_id_document_number; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX "IX_customers_tenant_id_document_number" ON nexo.customers USING btree (tenant_id, document_number);


--
-- Name: IX_customers_tenant_id_name; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_customers_tenant_id_name" ON nexo.customers USING btree (tenant_id, name);


--
-- Name: IX_financial_accounts_parent_account_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_financial_accounts_parent_account_id" ON nexo.financial_accounts USING btree (parent_account_id);


--
-- Name: IX_financial_accounts_tenant_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_financial_accounts_tenant_id" ON nexo.financial_accounts USING btree (tenant_id);


--
-- Name: IX_financial_accounts_tenant_id_code; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX "IX_financial_accounts_tenant_id_code" ON nexo.financial_accounts USING btree (tenant_id, code);


--
-- Name: IX_financial_transactions_financial_account_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_financial_transactions_financial_account_id" ON nexo.financial_transactions USING btree (financial_account_id);


--
-- Name: IX_financial_transactions_reference_type_reference_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_financial_transactions_reference_type_reference_id" ON nexo.financial_transactions USING btree (reference_type, reference_id);


--
-- Name: IX_financial_transactions_tenant_id_due_date; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_financial_transactions_tenant_id_due_date" ON nexo.financial_transactions USING btree (tenant_id, due_date);


--
-- Name: IX_financial_transactions_tenant_id_financial_account_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_financial_transactions_tenant_id_financial_account_id" ON nexo.financial_transactions USING btree (tenant_id, financial_account_id);


--
-- Name: IX_financial_transactions_tenant_id_status; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_financial_transactions_tenant_id_status" ON nexo.financial_transactions USING btree (tenant_id, status);


--
-- Name: IX_module_definitions_key; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX "IX_module_definitions_key" ON nexo.module_definitions USING btree (key);


--
-- Name: IX_module_subscriptions_TenantId1; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_module_subscriptions_TenantId1" ON nexo.module_subscriptions USING btree ("TenantId1");


--
-- Name: IX_module_subscriptions_granted_by_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_module_subscriptions_granted_by_id" ON nexo.module_subscriptions USING btree (granted_by_id);


--
-- Name: IX_module_subscriptions_status_current_period_end; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_module_subscriptions_status_current_period_end" ON nexo.module_subscriptions USING btree (status, current_period_end);


--
-- Name: IX_module_subscriptions_tenant_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_module_subscriptions_tenant_id" ON nexo.module_subscriptions USING btree (tenant_id);


--
-- Name: IX_module_subscriptions_tenant_id_module_key; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX "IX_module_subscriptions_tenant_id_module_key" ON nexo.module_subscriptions USING btree (tenant_id, module_key);


--
-- Name: IX_platform_users_email; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX "IX_platform_users_email" ON nexo.platform_users USING btree (email);


--
-- Name: IX_products_category_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_products_category_id" ON nexo.products USING btree (category_id);


--
-- Name: IX_products_store_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_products_store_id" ON nexo.products USING btree (store_id);


--
-- Name: IX_products_tenant_id_store_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_products_tenant_id_store_id" ON nexo.products USING btree (tenant_id, store_id);


--
-- Name: IX_products_tenant_id_store_id_barcode; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_products_tenant_id_store_id_barcode" ON nexo.products USING btree (tenant_id, store_id, barcode);


--
-- Name: IX_products_tenant_id_store_id_code; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX "IX_products_tenant_id_store_id_code" ON nexo.products USING btree (tenant_id, store_id, code);


--
-- Name: IX_rest_order_items_product_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_rest_order_items_product_id" ON nexo.rest_order_items USING btree (product_id);


--
-- Name: IX_rest_orders_sale_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_rest_orders_sale_id" ON nexo.rest_orders USING btree (sale_id);


--
-- Name: IX_rest_orders_table_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_rest_orders_table_id" ON nexo.rest_orders USING btree (table_id);


--
-- Name: IX_rest_recipe_cards_product_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_rest_recipe_cards_product_id" ON nexo.rest_recipe_cards USING btree (product_id);


--
-- Name: IX_rest_recipe_ingredients_ingredient_product_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_rest_recipe_ingredients_ingredient_product_id" ON nexo.rest_recipe_ingredients USING btree (ingredient_product_id);


--
-- Name: IX_rest_tables_area_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_rest_tables_area_id" ON nexo.rest_tables USING btree (area_id);


--
-- Name: IX_ret_customer_price_lists_customer_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_ret_customer_price_lists_customer_id" ON nexo.ret_customer_price_lists USING btree (customer_id);


--
-- Name: IX_ret_customer_price_lists_price_list_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_ret_customer_price_lists_price_list_id" ON nexo.ret_customer_price_lists USING btree (price_list_id);


--
-- Name: IX_ret_price_list_items_product_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_ret_price_list_items_product_id" ON nexo.ret_price_list_items USING btree (product_id);


--
-- Name: IX_ret_purchase_items_product_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_ret_purchase_items_product_id" ON nexo.ret_purchase_items USING btree (product_id);


--
-- Name: IX_ret_purchase_items_purchase_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_ret_purchase_items_purchase_id" ON nexo.ret_purchase_items USING btree (purchase_id);


--
-- Name: IX_ret_purchases_supplier_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_ret_purchases_supplier_id" ON nexo.ret_purchases USING btree (supplier_id);


--
-- Name: IX_sale_items_product_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_sale_items_product_id" ON nexo.sale_items USING btree (product_id);


--
-- Name: IX_sale_items_sale_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_sale_items_sale_id" ON nexo.sale_items USING btree (sale_id);


--
-- Name: IX_sale_items_tenant_id_product_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_sale_items_tenant_id_product_id" ON nexo.sale_items USING btree (tenant_id, product_id);


--
-- Name: IX_sale_items_tenant_id_sale_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_sale_items_tenant_id_sale_id" ON nexo.sale_items USING btree (tenant_id, sale_id);


--
-- Name: IX_sale_payments_sale_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_sale_payments_sale_id" ON nexo.sale_payments USING btree (sale_id);


--
-- Name: IX_sale_payments_tenant_id_sale_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_sale_payments_tenant_id_sale_id" ON nexo.sale_payments USING btree (tenant_id, sale_id);


--
-- Name: IX_sales_cash_session_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_sales_cash_session_id" ON nexo.sales USING btree (cash_session_id);


--
-- Name: IX_sales_customer_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_sales_customer_id" ON nexo.sales USING btree (customer_id);


--
-- Name: IX_sales_sold_by_user_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_sales_sold_by_user_id" ON nexo.sales USING btree (sold_by_user_id);


--
-- Name: IX_sales_store_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_sales_store_id" ON nexo.sales USING btree (store_id);


--
-- Name: IX_sales_tenant_id_store_id_created_at; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_sales_tenant_id_store_id_created_at" ON nexo.sales USING btree (tenant_id, store_id, created_at);


--
-- Name: IX_sales_tenant_id_store_id_customer_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_sales_tenant_id_store_id_customer_id" ON nexo.sales USING btree (tenant_id, store_id, customer_id);


--
-- Name: IX_sales_tenant_id_store_id_number; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX "IX_sales_tenant_id_store_id_number" ON nexo.sales USING btree (tenant_id, store_id, number);


--
-- Name: IX_sales_tenant_id_store_id_status; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_sales_tenant_id_store_id_status" ON nexo.sales USING btree (tenant_id, store_id, status);


--
-- Name: IX_stock_items_product_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX "IX_stock_items_product_id" ON nexo.stock_items USING btree (product_id);


--
-- Name: IX_stock_items_store_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_stock_items_store_id" ON nexo.stock_items USING btree (store_id);


--
-- Name: IX_stock_items_tenant_id_store_id_product_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX "IX_stock_items_tenant_id_store_id_product_id" ON nexo.stock_items USING btree (tenant_id, store_id, product_id);


--
-- Name: IX_stock_movements_product_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_stock_movements_product_id" ON nexo.stock_movements USING btree (product_id);


--
-- Name: IX_stock_movements_reference_type_reference_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_stock_movements_reference_type_reference_id" ON nexo.stock_movements USING btree (reference_type, reference_id);


--
-- Name: IX_stock_movements_store_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_stock_movements_store_id" ON nexo.stock_movements USING btree (store_id);


--
-- Name: IX_stock_movements_tenant_id_store_id_created_at; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_stock_movements_tenant_id_store_id_created_at" ON nexo.stock_movements USING btree (tenant_id, store_id, created_at);


--
-- Name: IX_stock_movements_tenant_id_store_id_product_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_stock_movements_tenant_id_store_id_product_id" ON nexo.stock_movements USING btree (tenant_id, store_id, product_id);


--
-- Name: IX_stores_module_subscription_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_stores_module_subscription_id" ON nexo.stores USING btree (module_subscription_id);


--
-- Name: IX_stores_tenant_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_stores_tenant_id" ON nexo.stores USING btree (tenant_id);


--
-- Name: IX_stores_tenant_id_slug; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX "IX_stores_tenant_id_slug" ON nexo.stores USING btree (tenant_id, slug);


--
-- Name: IX_suppliers_tenant_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_suppliers_tenant_id" ON nexo.suppliers USING btree (tenant_id);


--
-- Name: IX_suppliers_tenant_id_document_number; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX "IX_suppliers_tenant_id_document_number" ON nexo.suppliers USING btree (tenant_id, document_number);


--
-- Name: IX_tenants_slug; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX "IX_tenants_slug" ON nexo.tenants USING btree (slug);


--
-- Name: IX_tenants_status; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_tenants_status" ON nexo.tenants USING btree (status);


--
-- Name: IX_tenants_stripe_customer_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX "IX_tenants_stripe_customer_id" ON nexo.tenants USING btree (stripe_customer_id);


--
-- Name: IX_tenants_tax_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX "IX_tenants_tax_id" ON nexo.tenants USING btree (tax_id);


--
-- Name: IX_users_TenantId1; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_users_TenantId1" ON nexo.users USING btree ("TenantId1");


--
-- Name: IX_users_tenant_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX "IX_users_tenant_id" ON nexo.users USING btree (tenant_id);


--
-- Name: IX_users_tenant_id_email; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX "IX_users_tenant_id_email" ON nexo.users USING btree (tenant_id, email);


--
-- Name: IX_users_tenant_id_login; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX "IX_users_tenant_id_login" ON nexo.users USING btree (tenant_id, login);


--
-- Name: ix_cash_sessions_store_user_status; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_cash_sessions_store_user_status ON nexo.cash_sessions USING btree (tenant_id, store_id, opened_by_user_id, status);


--
-- Name: ix_food_service_settings_store_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_food_service_settings_store_id ON nexo.food_service_settings USING btree (store_id);


--
-- Name: ix_food_service_settings_tenant_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_food_service_settings_tenant_id ON nexo.food_service_settings USING btree (tenant_id);


--
-- Name: ix_food_service_settings_tenant_store; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX ix_food_service_settings_tenant_store ON nexo.food_service_settings USING btree (tenant_id, store_id);


--
-- Name: ix_product_modifier_groups_product_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_product_modifier_groups_product_id ON nexo.product_modifier_groups USING btree (product_id);


--
-- Name: ix_product_modifier_groups_tenant_product; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_product_modifier_groups_tenant_product ON nexo.product_modifier_groups USING btree (tenant_id, product_id);


--
-- Name: ix_product_modifiers_group_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_product_modifiers_group_id ON nexo.product_modifiers USING btree (group_id);


--
-- Name: ix_product_modifiers_tenant_group; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_product_modifiers_tenant_group ON nexo.product_modifiers USING btree (tenant_id, group_id);


--
-- Name: ix_rest_areas_store_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_rest_areas_store_id ON nexo.rest_areas USING btree (store_id);


--
-- Name: ix_rest_areas_tenant_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_rest_areas_tenant_id ON nexo.rest_areas USING btree (tenant_id);


--
-- Name: ix_rest_areas_tenant_store_name; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX ix_rest_areas_tenant_store_name ON nexo.rest_areas USING btree (tenant_id, store_id, name);


--
-- Name: ix_rest_order_item_modifiers_item; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_rest_order_item_modifiers_item ON nexo.rest_order_item_modifiers USING btree (tenant_id, order_item_id);


--
-- Name: ix_rest_order_item_modifiers_order_item_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_rest_order_item_modifiers_order_item_id ON nexo.rest_order_item_modifiers USING btree (order_item_id);


--
-- Name: ix_rest_order_items_order_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_rest_order_items_order_id ON nexo.rest_order_items USING btree (order_id);


--
-- Name: ix_rest_order_items_tenant_order; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_rest_order_items_tenant_order ON nexo.rest_order_items USING btree (tenant_id, order_id);


--
-- Name: ix_rest_order_items_tenant_product; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_rest_order_items_tenant_product ON nexo.rest_order_items USING btree (tenant_id, product_id);


--
-- Name: ix_rest_order_items_tenant_status; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_rest_order_items_tenant_status ON nexo.rest_order_items USING btree (tenant_id, status);


--
-- Name: ix_rest_orders_one_active_per_table; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX ix_rest_orders_one_active_per_table ON nexo.rest_orders USING btree (tenant_id, store_id, table_id) WHERE ((table_id IS NOT NULL) AND ((status)::text <> ALL ((ARRAY['Closed'::character varying, 'Paid'::character varying, 'Cancelled'::character varying])::text[])));


--
-- Name: ix_rest_orders_store_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_rest_orders_store_id ON nexo.rest_orders USING btree (store_id);


--
-- Name: ix_rest_orders_tenant_store_created_at; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_rest_orders_tenant_store_created_at ON nexo.rest_orders USING btree (tenant_id, store_id, created_at);


--
-- Name: ix_rest_orders_tenant_store_number; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX ix_rest_orders_tenant_store_number ON nexo.rest_orders USING btree (tenant_id, store_id, order_number);


--
-- Name: ix_rest_orders_tenant_store_status; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_rest_orders_tenant_store_status ON nexo.rest_orders USING btree (tenant_id, store_id, status);


--
-- Name: ix_rest_recipe_cards_store_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_rest_recipe_cards_store_id ON nexo.rest_recipe_cards USING btree (store_id);


--
-- Name: ix_rest_recipe_cards_tenant_store_product; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX ix_rest_recipe_cards_tenant_store_product ON nexo.rest_recipe_cards USING btree (tenant_id, store_id, product_id);


--
-- Name: ix_rest_recipe_ingredients_card_product; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX ix_rest_recipe_ingredients_card_product ON nexo.rest_recipe_ingredients USING btree (recipe_card_id, ingredient_product_id);


--
-- Name: ix_rest_recipe_ingredients_tenant_card; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_rest_recipe_ingredients_tenant_card ON nexo.rest_recipe_ingredients USING btree (tenant_id, recipe_card_id);


--
-- Name: ix_rest_tables_store_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_rest_tables_store_id ON nexo.rest_tables USING btree (store_id);


--
-- Name: ix_rest_tables_tenant_store_area; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_rest_tables_tenant_store_area ON nexo.rest_tables USING btree (tenant_id, store_id, area_id);


--
-- Name: ix_rest_tables_tenant_store_number; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX ix_rest_tables_tenant_store_number ON nexo.rest_tables USING btree (tenant_id, store_id, number);


--
-- Name: ix_rest_tables_tenant_store_status; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_rest_tables_tenant_store_status ON nexo.rest_tables USING btree (tenant_id, store_id, status);


--
-- Name: ix_ret_customer_price_lists_tenant_customer; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX ix_ret_customer_price_lists_tenant_customer ON nexo.ret_customer_price_lists USING btree (tenant_id, customer_id);


--
-- Name: ix_ret_price_list_items_list_product; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX ix_ret_price_list_items_list_product ON nexo.ret_price_list_items USING btree (price_list_id, product_id);


--
-- Name: ix_ret_price_list_items_tenant_id_product; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_ret_price_list_items_tenant_id_product ON nexo.ret_price_list_items USING btree (tenant_id, product_id);


--
-- Name: ix_ret_price_lists_tenant_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_ret_price_lists_tenant_id ON nexo.ret_price_lists USING btree (tenant_id);


--
-- Name: ix_ret_price_lists_tenant_id_is_default; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_ret_price_lists_tenant_id_is_default ON nexo.ret_price_lists USING btree (tenant_id, is_default);


--
-- Name: ix_ret_purchase_items_tenant_id_product_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_ret_purchase_items_tenant_id_product_id ON nexo.ret_purchase_items USING btree (tenant_id, product_id);


--
-- Name: ix_ret_purchase_items_tenant_id_purchase_id; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_ret_purchase_items_tenant_id_purchase_id ON nexo.ret_purchase_items USING btree (tenant_id, purchase_id);


--
-- Name: ix_ret_purchases_tenant_id_created_at; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_ret_purchases_tenant_id_created_at ON nexo.ret_purchases USING btree (tenant_id, created_at);


--
-- Name: ix_ret_purchases_tenant_id_number; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE UNIQUE INDEX ix_ret_purchases_tenant_id_number ON nexo.ret_purchases USING btree (tenant_id, purchase_number);


--
-- Name: ix_ret_purchases_tenant_id_status; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_ret_purchases_tenant_id_status ON nexo.ret_purchases USING btree (tenant_id, status);


--
-- Name: ix_ret_purchases_tenant_id_supplier; Type: INDEX; Schema: nexo; Owner: nexo_user
--

CREATE INDEX ix_ret_purchases_tenant_id_supplier ON nexo.ret_purchases USING btree (tenant_id, supplier_id);


--
-- Name: app_settings FK_app_settings_tenants_TenantId1; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.app_settings
    ADD CONSTRAINT "FK_app_settings_tenants_TenantId1" FOREIGN KEY ("TenantId1") REFERENCES nexo.tenants(id);


--
-- Name: app_settings FK_app_settings_tenants_tenant_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.app_settings
    ADD CONSTRAINT "FK_app_settings_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: cash_movements FK_cash_movements_cash_sessions_cash_session_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.cash_movements
    ADD CONSTRAINT "FK_cash_movements_cash_sessions_cash_session_id" FOREIGN KEY (cash_session_id) REFERENCES nexo.cash_sessions(id) ON DELETE RESTRICT;


--
-- Name: cash_movements FK_cash_movements_tenants_tenant_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.cash_movements
    ADD CONSTRAINT "FK_cash_movements_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: cash_sessions FK_cash_sessions_stores_store_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.cash_sessions
    ADD CONSTRAINT "FK_cash_sessions_stores_store_id" FOREIGN KEY (store_id) REFERENCES nexo.stores(id) ON DELETE RESTRICT;


--
-- Name: cash_sessions FK_cash_sessions_tenants_tenant_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.cash_sessions
    ADD CONSTRAINT "FK_cash_sessions_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: cash_sessions FK_cash_sessions_users_closed_by_user_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.cash_sessions
    ADD CONSTRAINT "FK_cash_sessions_users_closed_by_user_id" FOREIGN KEY (closed_by_user_id) REFERENCES nexo.users(id) ON DELETE RESTRICT;


--
-- Name: cash_sessions FK_cash_sessions_users_opened_by_user_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.cash_sessions
    ADD CONSTRAINT "FK_cash_sessions_users_opened_by_user_id" FOREIGN KEY (opened_by_user_id) REFERENCES nexo.users(id) ON DELETE RESTRICT;


--
-- Name: categories FK_categories_categories_parent_category_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.categories
    ADD CONSTRAINT "FK_categories_categories_parent_category_id" FOREIGN KEY (parent_category_id) REFERENCES nexo.categories(id) ON DELETE RESTRICT;


--
-- Name: categories FK_categories_tenants_tenant_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.categories
    ADD CONSTRAINT "FK_categories_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: customers FK_customers_stores_store_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.customers
    ADD CONSTRAINT "FK_customers_stores_store_id" FOREIGN KEY (store_id) REFERENCES nexo.stores(id) ON DELETE SET NULL;


--
-- Name: customers FK_customers_tenants_tenant_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.customers
    ADD CONSTRAINT "FK_customers_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: financial_accounts FK_financial_accounts_financial_accounts_parent_account_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.financial_accounts
    ADD CONSTRAINT "FK_financial_accounts_financial_accounts_parent_account_id" FOREIGN KEY (parent_account_id) REFERENCES nexo.financial_accounts(id) ON DELETE RESTRICT;


--
-- Name: financial_accounts FK_financial_accounts_tenants_tenant_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.financial_accounts
    ADD CONSTRAINT "FK_financial_accounts_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: financial_transactions FK_financial_transactions_financial_accounts_financial_account~; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.financial_transactions
    ADD CONSTRAINT "FK_financial_transactions_financial_accounts_financial_account~" FOREIGN KEY (financial_account_id) REFERENCES nexo.financial_accounts(id) ON DELETE RESTRICT;


--
-- Name: financial_transactions FK_financial_transactions_tenants_tenant_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.financial_transactions
    ADD CONSTRAINT "FK_financial_transactions_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: module_subscriptions FK_module_subscriptions_platform_users_granted_by_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.module_subscriptions
    ADD CONSTRAINT "FK_module_subscriptions_platform_users_granted_by_id" FOREIGN KEY (granted_by_id) REFERENCES nexo.platform_users(id) ON DELETE SET NULL;


--
-- Name: module_subscriptions FK_module_subscriptions_tenants_TenantId1; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.module_subscriptions
    ADD CONSTRAINT "FK_module_subscriptions_tenants_TenantId1" FOREIGN KEY ("TenantId1") REFERENCES nexo.tenants(id);


--
-- Name: module_subscriptions FK_module_subscriptions_tenants_tenant_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.module_subscriptions
    ADD CONSTRAINT "FK_module_subscriptions_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: products FK_products_categories_category_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.products
    ADD CONSTRAINT "FK_products_categories_category_id" FOREIGN KEY (category_id) REFERENCES nexo.categories(id) ON DELETE SET NULL;


--
-- Name: products FK_products_stores_store_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.products
    ADD CONSTRAINT "FK_products_stores_store_id" FOREIGN KEY (store_id) REFERENCES nexo.stores(id) ON DELETE RESTRICT;


--
-- Name: products FK_products_tenants_tenant_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.products
    ADD CONSTRAINT "FK_products_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: rest_order_items FK_rest_order_items_rest_orders_order_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_order_items
    ADD CONSTRAINT "FK_rest_order_items_rest_orders_order_id" FOREIGN KEY (order_id) REFERENCES nexo.rest_orders(id) ON DELETE CASCADE;


--
-- Name: rest_recipe_ingredients FK_rest_recipe_ingredients_rest_recipe_cards_recipe_card_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_recipe_ingredients
    ADD CONSTRAINT "FK_rest_recipe_ingredients_rest_recipe_cards_recipe_card_id" FOREIGN KEY (recipe_card_id) REFERENCES nexo.rest_recipe_cards(id) ON DELETE CASCADE;


--
-- Name: ret_price_list_items FK_ret_price_list_items_ret_price_lists_price_list_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.ret_price_list_items
    ADD CONSTRAINT "FK_ret_price_list_items_ret_price_lists_price_list_id" FOREIGN KEY (price_list_id) REFERENCES nexo.ret_price_lists(id) ON DELETE CASCADE;


--
-- Name: ret_purchase_items FK_ret_purchase_items_ret_purchases_purchase_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.ret_purchase_items
    ADD CONSTRAINT "FK_ret_purchase_items_ret_purchases_purchase_id" FOREIGN KEY (purchase_id) REFERENCES nexo.ret_purchases(id) ON DELETE CASCADE;


--
-- Name: sale_items FK_sale_items_products_product_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.sale_items
    ADD CONSTRAINT "FK_sale_items_products_product_id" FOREIGN KEY (product_id) REFERENCES nexo.products(id) ON DELETE RESTRICT;


--
-- Name: sale_items FK_sale_items_sales_sale_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.sale_items
    ADD CONSTRAINT "FK_sale_items_sales_sale_id" FOREIGN KEY (sale_id) REFERENCES nexo.sales(id) ON DELETE CASCADE;


--
-- Name: sale_items FK_sale_items_tenants_tenant_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.sale_items
    ADD CONSTRAINT "FK_sale_items_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: sale_payments FK_sale_payments_sales_sale_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.sale_payments
    ADD CONSTRAINT "FK_sale_payments_sales_sale_id" FOREIGN KEY (sale_id) REFERENCES nexo.sales(id) ON DELETE CASCADE;


--
-- Name: sale_payments FK_sale_payments_tenants_tenant_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.sale_payments
    ADD CONSTRAINT "FK_sale_payments_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: sales FK_sales_cash_sessions_cash_session_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.sales
    ADD CONSTRAINT "FK_sales_cash_sessions_cash_session_id" FOREIGN KEY (cash_session_id) REFERENCES nexo.cash_sessions(id) ON DELETE RESTRICT;


--
-- Name: sales FK_sales_customers_customer_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.sales
    ADD CONSTRAINT "FK_sales_customers_customer_id" FOREIGN KEY (customer_id) REFERENCES nexo.customers(id) ON DELETE RESTRICT;


--
-- Name: sales FK_sales_stores_store_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.sales
    ADD CONSTRAINT "FK_sales_stores_store_id" FOREIGN KEY (store_id) REFERENCES nexo.stores(id) ON DELETE RESTRICT;


--
-- Name: sales FK_sales_tenants_tenant_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.sales
    ADD CONSTRAINT "FK_sales_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: sales FK_sales_users_sold_by_user_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.sales
    ADD CONSTRAINT "FK_sales_users_sold_by_user_id" FOREIGN KEY (sold_by_user_id) REFERENCES nexo.users(id) ON DELETE RESTRICT;


--
-- Name: stock_items FK_stock_items_products_product_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.stock_items
    ADD CONSTRAINT "FK_stock_items_products_product_id" FOREIGN KEY (product_id) REFERENCES nexo.products(id) ON DELETE CASCADE;


--
-- Name: stock_items FK_stock_items_stores_store_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.stock_items
    ADD CONSTRAINT "FK_stock_items_stores_store_id" FOREIGN KEY (store_id) REFERENCES nexo.stores(id) ON DELETE RESTRICT;


--
-- Name: stock_items FK_stock_items_tenants_tenant_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.stock_items
    ADD CONSTRAINT "FK_stock_items_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: stock_movements FK_stock_movements_products_product_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.stock_movements
    ADD CONSTRAINT "FK_stock_movements_products_product_id" FOREIGN KEY (product_id) REFERENCES nexo.products(id) ON DELETE RESTRICT;


--
-- Name: stock_movements FK_stock_movements_stores_store_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.stock_movements
    ADD CONSTRAINT "FK_stock_movements_stores_store_id" FOREIGN KEY (store_id) REFERENCES nexo.stores(id) ON DELETE RESTRICT;


--
-- Name: stock_movements FK_stock_movements_tenants_tenant_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.stock_movements
    ADD CONSTRAINT "FK_stock_movements_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: stores FK_stores_module_subscriptions_module_subscription_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.stores
    ADD CONSTRAINT "FK_stores_module_subscriptions_module_subscription_id" FOREIGN KEY (module_subscription_id) REFERENCES nexo.module_subscriptions(id) ON DELETE SET NULL;


--
-- Name: stores FK_stores_tenants_tenant_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.stores
    ADD CONSTRAINT "FK_stores_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: suppliers FK_suppliers_tenants_tenant_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.suppliers
    ADD CONSTRAINT "FK_suppliers_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: users FK_users_tenants_TenantId1; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.users
    ADD CONSTRAINT "FK_users_tenants_TenantId1" FOREIGN KEY ("TenantId1") REFERENCES nexo.tenants(id);


--
-- Name: users FK_users_tenants_tenant_id; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.users
    ADD CONSTRAINT "FK_users_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: food_service_settings fk_food_service_settings_stores; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.food_service_settings
    ADD CONSTRAINT fk_food_service_settings_stores FOREIGN KEY (store_id) REFERENCES nexo.stores(id) ON DELETE RESTRICT;


--
-- Name: food_service_settings fk_food_service_settings_tenants; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.food_service_settings
    ADD CONSTRAINT fk_food_service_settings_tenants FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: product_modifier_groups fk_product_modifier_groups_products; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.product_modifier_groups
    ADD CONSTRAINT fk_product_modifier_groups_products FOREIGN KEY (product_id) REFERENCES nexo.products(id) ON DELETE CASCADE;


--
-- Name: product_modifier_groups fk_product_modifier_groups_tenants; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.product_modifier_groups
    ADD CONSTRAINT fk_product_modifier_groups_tenants FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: product_modifiers fk_product_modifiers_product_modifier_groups; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.product_modifiers
    ADD CONSTRAINT fk_product_modifiers_product_modifier_groups FOREIGN KEY (group_id) REFERENCES nexo.product_modifier_groups(id) ON DELETE CASCADE;


--
-- Name: product_modifiers fk_product_modifiers_tenants; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.product_modifiers
    ADD CONSTRAINT fk_product_modifiers_tenants FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: rest_areas fk_rest_areas_stores; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_areas
    ADD CONSTRAINT fk_rest_areas_stores FOREIGN KEY (store_id) REFERENCES nexo.stores(id) ON DELETE RESTRICT;


--
-- Name: rest_areas fk_rest_areas_tenants; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_areas
    ADD CONSTRAINT fk_rest_areas_tenants FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: rest_order_item_modifiers fk_rest_order_item_modifiers_items; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_order_item_modifiers
    ADD CONSTRAINT fk_rest_order_item_modifiers_items FOREIGN KEY (order_item_id) REFERENCES nexo.rest_order_items(id) ON DELETE CASCADE;


--
-- Name: rest_order_item_modifiers fk_rest_order_item_modifiers_tenants; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_order_item_modifiers
    ADD CONSTRAINT fk_rest_order_item_modifiers_tenants FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: rest_order_items fk_rest_order_items_products; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_order_items
    ADD CONSTRAINT fk_rest_order_items_products FOREIGN KEY (product_id) REFERENCES nexo.products(id) ON DELETE RESTRICT;


--
-- Name: rest_order_items fk_rest_order_items_tenants; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_order_items
    ADD CONSTRAINT fk_rest_order_items_tenants FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: rest_orders fk_rest_orders_sales; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_orders
    ADD CONSTRAINT fk_rest_orders_sales FOREIGN KEY (sale_id) REFERENCES nexo.sales(id) ON DELETE RESTRICT;


--
-- Name: rest_orders fk_rest_orders_stores; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_orders
    ADD CONSTRAINT fk_rest_orders_stores FOREIGN KEY (store_id) REFERENCES nexo.stores(id) ON DELETE RESTRICT;


--
-- Name: rest_orders fk_rest_orders_tables; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_orders
    ADD CONSTRAINT fk_rest_orders_tables FOREIGN KEY (table_id) REFERENCES nexo.rest_tables(id) ON DELETE RESTRICT;


--
-- Name: rest_orders fk_rest_orders_tenants; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_orders
    ADD CONSTRAINT fk_rest_orders_tenants FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: rest_recipe_cards fk_rest_recipe_cards_products; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_recipe_cards
    ADD CONSTRAINT fk_rest_recipe_cards_products FOREIGN KEY (product_id) REFERENCES nexo.products(id) ON DELETE RESTRICT;


--
-- Name: rest_recipe_cards fk_rest_recipe_cards_stores; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_recipe_cards
    ADD CONSTRAINT fk_rest_recipe_cards_stores FOREIGN KEY (store_id) REFERENCES nexo.stores(id) ON DELETE RESTRICT;


--
-- Name: rest_recipe_cards fk_rest_recipe_cards_tenants; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_recipe_cards
    ADD CONSTRAINT fk_rest_recipe_cards_tenants FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: rest_recipe_ingredients fk_rest_recipe_ingredients_products; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_recipe_ingredients
    ADD CONSTRAINT fk_rest_recipe_ingredients_products FOREIGN KEY (ingredient_product_id) REFERENCES nexo.products(id) ON DELETE RESTRICT;


--
-- Name: rest_recipe_ingredients fk_rest_recipe_ingredients_tenants; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_recipe_ingredients
    ADD CONSTRAINT fk_rest_recipe_ingredients_tenants FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: rest_tables fk_rest_tables_areas; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_tables
    ADD CONSTRAINT fk_rest_tables_areas FOREIGN KEY (area_id) REFERENCES nexo.rest_areas(id) ON DELETE RESTRICT;


--
-- Name: rest_tables fk_rest_tables_stores; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_tables
    ADD CONSTRAINT fk_rest_tables_stores FOREIGN KEY (store_id) REFERENCES nexo.stores(id) ON DELETE RESTRICT;


--
-- Name: rest_tables fk_rest_tables_tenants; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.rest_tables
    ADD CONSTRAINT fk_rest_tables_tenants FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: ret_customer_price_lists fk_ret_customer_price_lists_customers; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.ret_customer_price_lists
    ADD CONSTRAINT fk_ret_customer_price_lists_customers FOREIGN KEY (customer_id) REFERENCES nexo.customers(id) ON DELETE CASCADE;


--
-- Name: ret_customer_price_lists fk_ret_customer_price_lists_price_lists; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.ret_customer_price_lists
    ADD CONSTRAINT fk_ret_customer_price_lists_price_lists FOREIGN KEY (price_list_id) REFERENCES nexo.ret_price_lists(id) ON DELETE CASCADE;


--
-- Name: ret_customer_price_lists fk_ret_customer_price_lists_tenants; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.ret_customer_price_lists
    ADD CONSTRAINT fk_ret_customer_price_lists_tenants FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: ret_price_list_items fk_ret_price_list_items_products; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.ret_price_list_items
    ADD CONSTRAINT fk_ret_price_list_items_products FOREIGN KEY (product_id) REFERENCES nexo.products(id) ON DELETE CASCADE;


--
-- Name: ret_price_list_items fk_ret_price_list_items_tenants; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.ret_price_list_items
    ADD CONSTRAINT fk_ret_price_list_items_tenants FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: ret_price_lists fk_ret_price_lists_tenants; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.ret_price_lists
    ADD CONSTRAINT fk_ret_price_lists_tenants FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: ret_purchase_items fk_ret_purchase_items_products; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.ret_purchase_items
    ADD CONSTRAINT fk_ret_purchase_items_products FOREIGN KEY (product_id) REFERENCES nexo.products(id) ON DELETE RESTRICT;


--
-- Name: ret_purchase_items fk_ret_purchase_items_tenants; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.ret_purchase_items
    ADD CONSTRAINT fk_ret_purchase_items_tenants FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- Name: ret_purchases fk_ret_purchases_suppliers; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.ret_purchases
    ADD CONSTRAINT fk_ret_purchases_suppliers FOREIGN KEY (supplier_id) REFERENCES nexo.suppliers(id) ON DELETE RESTRICT;


--
-- Name: ret_purchases fk_ret_purchases_tenants; Type: FK CONSTRAINT; Schema: nexo; Owner: nexo_user
--

ALTER TABLE ONLY nexo.ret_purchases
    ADD CONSTRAINT fk_ret_purchases_tenants FOREIGN KEY (tenant_id) REFERENCES nexo.tenants(id) ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

\unrestrict kLvFGy1iafBp6qPDsQkP7f6PsvcRk6P1tjbl03wzJYfgwqkNxmxyjCEfixf0YcE

