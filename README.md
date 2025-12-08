# 🎮 tamo-junto-games — API (Back-End) 🕹️

API desenvolvida para sustentar o projeto **tamo-junto-games**, uma plataforma voltada para gamers com o objetivo de organizar listas de jogos, registrar progresso, avaliações, interações entre jogadores e futuramente integrar com o Front-End do projeto.

Este repositório contém o **Back-End**, criado como base para estudos e evolução do sistema principal.

---

## 🔨 Tecnologias utilizadas

- **C#**
- **ASP.NET Core Web API**
- **DTOs**
- **CRUD básico**
- Em breve: **Banco de dados (SQL Server / PostgreSQL)**  
- Futuro: **Autenticação, Login/JWT, Middlewares, Serviços e Regras de negócio**

---

## 🏗️ Status do desenvolvimento

📌 Projeto em desenvolvimento

### ✔️ Fase atual
- Criação dos primeiros endpoints
- Implementação de GET / POST / PUT / DELETE (CRUD básico)
- Uso de DTOs para requisições e respostas

### 🎯 Próximos passos
- Criar banco de dados para persistência
- Implementar autenticação (registro/login)
- Conectar com o Front-End (outro repositório)
- Começar estrutura de jogos, lista de jogos e avaliações

---

## 🛠 Endpoints disponíveis atualmente

| Método | Rota | Descrição |
|-------|------|-----------|
| **GET** | `/api/usuarios` | Lista todos os usuários |
| **GET** | `/api/usuarios/{id}` | Retorna um usuário específico |
| **POST** | `/api/usuarios` | Cria um novo usuário |
| **PUT** | `/api/usuarios/{id}` | Atualiza dados de um usuário |
| **DELETE** | `/api/usuarios/{id}` | Remove um usuário |

---

## 🧪 Testes da API

Você pode testar usando **Thunder Client / Postman / Insomnia**  
ou pelo arquivo `Testes.http` dentro do projeto.

Exemplo de criação (POST):

```json
{
  "nomeCompleto": "Exemplo Nome",
  "apelido": "Nickname",
  "email": "exemplo@email.com",
  "confirmarEmail": "exemplo@email.com",
  "senha": "SenhaForte123",
  "confirmarSenha": "SenhaForte123"
}
```

---

## 💼 Repotórios do projeto

- 🌐 Front-End > https://github.com/FernandoORochaA/tamo-junto-games-web
- 💻 Back-End / API (este repositório)> https://github.com/FernandoORochaA/tamo-junto-games-api

---

## 🧙 Autor
**Fernando Rocha**
[GitHub](https://github.com/FernandoORochaA)

