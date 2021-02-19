function writeReleases() {

    document.write("<li><a class='black-text' href='https://github.com/indigo-san/BEditor/releases'>ダウンロード</a></li>");
    document.write("<li><a class='black-text' href='https://github.com/indigo-san/BEditor/releases/tag/v0.0.4-alpha'>0.0.4</a></li>");
    document.write("<li><a class='black-text' href='https://github.com/indigo-san/BEditor/releases/tag/v0.0.3-alpha'>0.0.3</a></li>");
    document.write("<li><a class='black-text' href='https://drive.google.com/file/d/15BZabYO3jz_bGCnBT3IyMnxiJWHLAb-o/view?usp=sharing'>0.0.2</a></li>");
    document.write("<li><a class='black-text' href='https://drive.google.com/file/d/19w8gj_la7JAaCQjlEVldbbpos9xyMjrL/view?usp=sharing'>0.0.1</a></li>");
}

document.write("<nav class='white lighten-1' role='navigation'>");
document.write("        <div class='nav-wrapper container'>");
document.write("        <a id='logo-container' href='./index.html' class='brand-logo deep-purple-text text-accent-4'>");
document.write("            <i class='material-icons'>movie</i>");
document.write("        </a>");

// PC Nav
document.write("        <ul class='right hide-on-med-and-down'>");
document.write("            <li><a class='deep-purple-text text-accent-4' href='https://github.com/indigo-san/BEditor'>GitHub</a></li>");
document.write("            <li><a class='dropdown-trigger deep-purple-text text-accent-4' href='#' data-target='dropdown1'>ダウンロード</a></li>");
document.write("            <li><a class='deep-purple-text text-accent-4' href='./documents.html'>ドキュメント</a></li>");
document.write("        </ul>");
document.write("        <ul id='dropdown1' class='dropdown-content'>");
writeReleases();
document.write("        </ul>");

// Mobile Nav
document.write("        <ul id='nav-mobile' class='sidenav collapsible collapsible-accordion'>");
document.write("            <li><a style='padding: 0 16px;' href='https://github.com/indigo-san/BEditor'>GitHub</a></li>");
document.write("            <li>");
document.write("                <div class='collapsible-header black-text'>ダウンロード</div>");
document.write("                <div class='collapsible-body'>");
document.write("                    <ul>");
writeReleases();
document.write("                    </ul>");
document.write("                </div>");
document.write("            </li>");
document.write("            <li><a style='padding: 0 16px;' href='./documents.html'>ドキュメント</a></li>");
document.write("        </ul>");

document.write("        <a href='#' data-target='nav-mobile' class='sidenav-trigger deep-purple-text text-accent-4'><i class='material-icons'>menu</i></a>");
document.write("    </div>");
document.write("</nav>");


$(document).ready(function () {
    $('.collapsible').collapsible();
    $('.dropdown-trigger').dropdown();
});
