﻿

create table "order" (
   o_id           int                  identity,
   total          decimal(12,2)        null default 0,
   constraint PK_ORDER primary key (o_id)
)

create table order_item (  
   oi_id                int                  IDENTITY,
   order_id             int                  NULL,
   product_id           INT                  NOT NULL,
   price            decimal(12,2)        not null default 0,
   constraint PK_ORDERITEM primary key (oi_id)
)

create table product (
    p_id       INT                     IDENTITY,
    name       nvarchar(200)           NOT NULL,
    CONSTRAINT PK_PRODUCT PRIMARY KEY (p_id)
)

alter table order_item
   add constraint FK_ORDERITE_REFERENCE_ORDER foreign key (order_id)
      references "order" (o_id)


ALTER TABLE order_item 
    ADD CONSTRAINT FK_PRODUCT_ITEM FOREIGN KEY (product_id)
        REFERENCES "product" (p_id)




CREATE TABLE actor (
    a_id    INT IDENTITY,
    name    nvarchar(200)   NOT NULL,
    photo   varbinary(10)  NULL,
    CONSTRAINT PK_ACTOR PRIMARY KEY (a_id)
)

CREATE TABLE movie (
    m_id    INT IDENTITY,
    name nvarchar(200) NOT NULL,
    [file]   varbinary(10) NULL,
    CONSTRAINT PK_MOVIE PRIMARY  KEY (m_id)
)

CREATE TABLE actor_movie (
    a_id INT NOT NULL,
    m_id INT NOT NULL,
    
    CONSTRAINT PK_ACTOR_MOVIE PRIMARY KEY (a_id, m_id)
)

ALTER TABLE actor_movie 
    ADD CONSTRAINT FK_ACTOR FOREIGN KEY (a_id)
        REFERENCES "actor" (a_id)
        
ALTER TABLE actor_movie 
    ADD CONSTRAINT FK_MOVIE FOREIGN KEY (m_id)
        REFERENCES "movie" (m_id)        



