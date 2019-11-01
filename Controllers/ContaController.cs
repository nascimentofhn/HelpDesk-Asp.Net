using HelpDeskTCC.Models;
using HelpDeskTCC.ViewModels;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.AspNet.Identity.EntityFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Microsoft.Owin.Security;

namespace HelpDeskTCC.Controllers
{
    public class ContaController : Controller
    {
        private UserManager<UsuarioAplicacao> _userManager;
        public UserManager<UsuarioAplicacao> UserManager
        {
            get
            {
                if (_userManager == null)
                {
                    var contextoOwin = HttpContext.GetOwinContext();
                    _userManager = contextoOwin.GetUserManager<UserManager<UsuarioAplicacao>>();
                }

                return _userManager;
            }

            set
            {
                _userManager = value;
            }
        }

        private SignInManager<UsuarioAplicacao, string> _signInManager;
        public SignInManager<UsuarioAplicacao, string> SignInManager
        {
            get
            {
                if (_signInManager == null)
                {
                    var contextoOwin = HttpContext.GetOwinContext();
                    _signInManager = contextoOwin.GetUserManager<SignInManager<UsuarioAplicacao, string>>();
                }

                return _signInManager;
            }
        }

        public IAuthenticationManager AuthenticationManager
        {
            get
            {
                var contextoOwin = Request.GetOwinContext();
                return contextoOwin.Authentication;
            }
        }

        // GET: Registrar
        public ActionResult Index()
        {
            return View();
        }

        public ActionResult Registrar()
        {
            return View();
        }

        [HttpPost]
        public async Task<ActionResult> Registrar(ContaRegistrarViewModel modelo)
        {
            if (ModelState.IsValid)
            {
                var usuarioExistente = await UserManager.FindByEmailAsync(modelo.Email);
                var emailJaCadastrado = usuarioExistente != null;

                if (emailJaCadastrado)
                    return RedirectToAction("Index", "Home");

                // Registramos o usuário
                var usuario = new UsuarioAplicacao
                {
                    Email = modelo.Email,
                    UserName = modelo.Email,
                    NomeCompleto = modelo.NomeCompleto
                };
                var resultado = await UserManager.CreateAsync(usuario, modelo.Senha);

                if (resultado.Succeeded)
                {
                    await EnviarEmailConfirmacaoAsync(usuario);
                    return View("AguardandoConfirmacao", usuario);
                }
                else
                    AdicionarErros(resultado);
            }

            // Algo de errado aconteceu. Mostraremos novamente esta view
            // com os erros de validação.
            return View(modelo);
        }

        public async Task<ActionResult> ConfirmacaoEmail(string usuarioId, string codigo)
        {
            // lógica de verificação de código
            if (usuarioId == null || codigo == null)
                return View("Error");

            var resultado = await UserManager.ConfirmEmailAsync(usuarioId, codigo);

            if (resultado.Succeeded)
                return View("EmailConfirmado");
            else
                return View("Error");
        }

        public ActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task<ActionResult> Login(ContaLoginViewModelo modelo)
        {
            if (!ModelState.IsValid)
                return View(modelo);

            var usuario = await UserManager.FindByEmailAsync(modelo.Email);
            if (usuario == null)
                return SenhaOuEmailIncorreto(modelo);

            var signInResultado = await SignInManager.PasswordSignInAsync(
                usuario.UserName,
                modelo.Senha,
                isPersistent: modelo.ContinuarLogado,
                shouldLockout: true);

            switch (signInResultado)
            {
                case SignInStatus.Success:

                    if (!usuario.EmailConfirmed)
                    {
                        AuthenticationManager.SignOut(DefaultAuthenticationTypes.ApplicationCookie);
                        return View("AguardandoConfirmacao");
                    }

                    return VerificaEmailELogaUsuario(usuario);
                case SignInStatus.LockedOut:
                    var senhaCorreta = await UserManager.CheckPasswordAsync(usuario, modelo.Senha);
                    if (senhaCorreta)
                    {
                        ModelState.AddModelError("", "Conta bloqueada!");
                        return View("Login", modelo);
                    }
                    return SenhaOuEmailIncorreto(modelo);
                default:
                    return SenhaOuEmailIncorreto(modelo);
            }
        }

        public ActionResult EsqueciSenha()
        {
            return View();
        }

        [HttpPost]
        public async Task<ActionResult> EsqueciSenha(ContaEsqueciSenhaViewModel modelo)
        {
            if (ModelState.IsValid)
            {
                var usuario = await UserManager.FindByEmailAsync(modelo.Email);

                if (usuario != null)
                {
                    var token = await UserManager.GeneratePasswordResetTokenAsync(usuario.Id);

                    var callbackUrl = Url.Action("ConfirmacaoAlteracaoSenha", "Conta",
                        new { usuarioId = usuario.Id, token = token }, Request.Url.Scheme);
                    await UserManager.SendEmailAsync(
                        usuario.Id,
                        "Gerenciador de Chamados - Alteração de Senha",
                        "Clique no link para alterar sua senha " + callbackUrl);

                }

                return View("EmailAlteracaoSenhaEnviado");
            }
            return View();
        }

        public ActionResult ConfirmacaoAlteracaoSenha(string usuarioId, string token)
        {
            var modelo = new ContaConfirmacaoAlteracaoSenhaViewModel
            {
                UsuarioId = usuarioId,
                Token = token
            };
            return View(modelo);
        }

        [HttpPost]
        public async Task<ActionResult> ConfirmacaoAlteracaoSenha(ContaConfirmacaoAlteracaoSenhaViewModel modelo)
        {
            if (ModelState.IsValid)
            {
                var resultadoAlteracao = await UserManager.ResetPasswordAsync(modelo.UsuarioId, modelo.Token, modelo.NovaSenha);

                if (resultadoAlteracao.Succeeded)
                {
                    return RedirectToAction("Index", "Home");
                }
                AdicionarErros(resultadoAlteracao);
            }
            return View();
        }

        [HttpPost]
        public ActionResult Logoff()
        {
            AuthenticationManager.SignOut(DefaultAuthenticationTypes.ApplicationCookie);
            return RedirectToAction("Index", "Home");
        }

        private ActionResult SenhaOuEmailIncorreto(ContaLoginViewModelo modelo)
        {
            ModelState.AddModelError("", "Senha ou email inválidos!");
            return View("Login", modelo);
        }

        private ActionResult VerificaEmailELogaUsuario(UsuarioAplicacao usuario)
        {
            if (usuario.EmailConfirmed)
                return RedirectToAction("Index", "Home");
            else
                return View("AguardandoConfirmacao", usuario);
        }

        private async Task EnviarEmailConfirmacaoAsync(UsuarioAplicacao usuario)
        {
            var codigo = await UserManager.GenerateEmailConfirmationTokenAsync(usuario.Id);
            var callbackUrl = Url.Action("ConfirmacaoEmail", "Conta", new { usuarioId = usuario.Id, codigo = codigo }, protocol: Request.Url.Scheme);
            await UserManager.SendEmailAsync(
                usuario.Id,
                "Bem vindo ao Gerenciador de Chamados!",
                "Confirme seu email clicando aqui: " + callbackUrl);
        }

        private void AdicionarErros(IdentityResult resultado)
        {
            foreach (var erro in resultado.Errors)
            {
                ModelState.AddModelError("", erro);
            }
        }
    }
}